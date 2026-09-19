using System.Net;
using System.Net.Http.Json;
using Shouldly;
using SplitEverything.Application.Contracts.Categories;
using SplitEverything.Application.Contracts.Expenses;
using SplitEverything.Domain.Common;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Infrastructure.Persistence.Seed;
using SplitEverything.Tests.Support;

namespace SplitEverything.Tests.Api;

public class CategoryEndpointTests(PostgresFixture fixture) : ApiTestBase(fixture)
{
    private async Task SeedGlobalAsync()
    {
        await using var db = NewContext();
        db.Categories.AddRange(CategorySeed.BuildGlobalCategories());
        await db.SaveChangesAsync();
    }

    private async Task<GroupDto> AGroupAsync()
    {
        var response = await Client.PostAsJsonAsync("/api/groups",
            new CreateGroupRequest("Roommates", "CAD", null, null, null, null), Json);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<GroupDto>(Json))!;
    }

    [Fact]
    public async Task A_group_reads_the_servers_list_until_it_has_its_own()
    {
        await SeedGlobalAsync();
        await SignInAsync();
        var group = await AGroupAsync();

        var categories = await Client.GetFromJsonAsync<List<CategoryDto>>(
            $"/api/groups/{group.Id}/categories", Json);

        categories!.Count.ShouldBe(CategorySeed.Categories.Count);
        categories.ShouldContain(category => category.Key == "groceries");
    }

    [Fact]
    public async Task A_member_can_set_it_and_the_group_keeps_that_list()
    {
        await SeedGlobalAsync();
        await SignInAsync();
        var group = await AGroupAsync();

        var response = await Client.PutAsJsonAsync($"/api/groups/{group.Id}/categories",
            new SetCategoriesRequest([
                new CategoryInputDto("Épicerie", Keywords: ["metro", "iga"]),
                new CategoryInputDto("Ski"),
            ]), Json);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var saved = (await response.Content.ReadFromJsonAsync<List<CategoryDto>>(Json))!;
        saved.Select(c => c.Key).ShouldBe(["epicerie", "ski"]);
        saved[0].Keywords.ShouldBe(["metro", "iga"]);

        var read = await Client.GetFromJsonAsync<List<CategoryDto>>(
            $"/api/groups/{group.Id}/categories", Json);
        read!.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Somebody_outside_the_group_is_refused_both_ways()
    {
        await SignInAsync();
        var group = await AGroupAsync();

        var stranger = await SignInAsAnotherUserAsync("Emma");

        (await stranger.GetAsync($"/api/groups/{group.Id}/categories")).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
        (await stranger.PutAsJsonAsync($"/api/groups/{group.Id}/categories",
            new SetCategoriesRequest([new CategoryInputDto("Ski")]), Json)).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_list_that_breaks_the_rules_is_a_400_rather_than_a_500()
    {
        await SignInAsync();
        var group = await AGroupAsync();

        var response = await Client.PutAsJsonAsync($"/api/groups/{group.Id}/categories",
            new SetCategoriesRequest([
                new CategoryInputDto("Ski"),
                new CategoryInputDto("ski"),
            ]), Json);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_servers_own_list_belongs_to_whoever_runs_it()
    {
        await SeedGlobalAsync();
        await SignInAsync("Admin", "admin@example.com");

        var emma = await SignInAsAnotherUserAsync("Emma");

        (await emma.GetAsync("/api/admin/categories")).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
        (await emma.PutAsJsonAsync("/api/admin/categories",
            new SetCategoriesRequest([new CategoryInputDto("Everything")]), Json)).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);

        var categories = await Client.GetFromJsonAsync<List<CategoryDto>>(
            "/api/admin/categories", Json);
        categories!.Count.ShouldBe(CategorySeed.Categories.Count);
    }

    [Fact]
    public async Task What_the_administrator_sets_is_what_a_new_group_starts_from()
    {
        await SeedGlobalAsync();
        await SignInAsync("Admin", "admin@example.com");

        await Client.PutAsJsonAsync("/api/admin/categories",
            new SetCategoriesRequest([new CategoryInputDto("Everything", Keywords: ["all"])]), Json);

        var group = await AGroupAsync();
        var categories = await Client.GetFromJsonAsync<List<CategoryDto>>(
            $"/api/groups/{group.Id}/categories", Json);

        categories!.ShouldHaveSingleItem().Key.ShouldBe("everything");
    }

    [Fact]
    public async Task An_expense_keeps_what_it_was_filed_under_through_the_api()
    {
        await SeedGlobalAsync();
        var me = await SignInAsync();
        var group = await AGroupAsync();
        var payer = group.Members.First(m => m.UserId == me.Id).Id;

        var response = await Client.PostAsJsonAsync("/api/expenses",
            new CreateExpenseRequest(
                group.Id, payer, "Metro", 62m, "CAD", DateTimeOffset.UtcNow, SplitType.Equal,
                [new SplitInputDto(payer, null)],
                null, null, null, null, null, null, null, "groceries"), Json);

        response.EnsureSuccessStatusCode();
        var expense = (await response.Content.ReadFromJsonAsync<ExpenseDto>(Json))!;

        expense.CategoryKey.ShouldBe("groceries");
    }
}
