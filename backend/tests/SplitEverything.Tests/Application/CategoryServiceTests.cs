using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Categories;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Application.Services;
using SplitEverything.Infrastructure.Persistence.Seed;
using SplitEverything.Infrastructure.Services;
using SplitEverything.Tests.Support;

namespace SplitEverything.Tests.Application;

public class CategoryServiceTests(PostgresFixture fixture) : ServiceTestBase(fixture)
{
    private IAdminService NobodyAdministers()
    {
        var admins = Substitute.For<IAdminService>();
        admins.IsAdminAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        return admins;
    }

    private IAdminService Administers(Guid userId)
    {
        var admins = Substitute.For<IAdminService>();
        admins.IsAdminAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        admins.IsAdminAsync(userId, Arg.Any<CancellationToken>()).Returns(true);
        return admins;
    }

    private async Task SeedGlobalAsync()
    {
        Db.Categories.AddRange(CategorySeed.BuildGlobalCategories());
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
    }

    private async Task<(Guid OwnerId, Guid MemberId, GroupDto Group)> AGroupAsync()
    {
        var owner = await TestData.SeedUserAsync(Db, "Nicolas", "nicolas@example.com");
        var other = await TestData.SeedUserAsync(Db, "Emma", "emma@example.com");

        var group = await Groups.CreateAsync(owner.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, null));
        await Groups.AddUserMemberAsync(owner.Id, group.Id, new AddUserMemberRequest(other.Id));

        return (owner.Id, other.Id, group);
    }

    private CategoryService NewService(IAdminService? admins = null)
        => new(Db, admins ?? NobodyAdministers());

    [Fact]
    public async Task A_group_that_has_said_nothing_gets_the_servers_list()
    {
        await SeedGlobalAsync();
        var (ownerId, _, group) = await AGroupAsync();

        var categories = await NewService().GetForGroupAsync(ownerId, group.Id);

        categories.Select(c => c.Key).ShouldContain("groceries");
        categories.Count.ShouldBe(CategorySeed.Categories.Count);
    }

    [Fact]
    public async Task The_built_in_list_arrives_with_the_words_that_fill_it_in()
    {
        await SeedGlobalAsync();
        var (ownerId, _, group) = await AGroupAsync();

        var categories = await NewService().GetForGroupAsync(ownerId, group.Id);
        var groceries = categories.Single(c => c.Key == "groceries");

        groceries.Keywords.ShouldContain("metro");
        groceries.Keywords.ShouldContain("iga");
    }

    [Fact]
    public async Task Editing_takes_a_copy_of_the_servers_list_at_that_moment()
    {
        await SeedGlobalAsync();
        var (ownerId, _, group) = await AGroupAsync();
        var service = NewService(Administers(ownerId));

        var theirs = (await service.GetForGroupAsync(ownerId, group.Id))
            .Select(c => new CategoryInputDto(c.Name, c.Key, c.IconName, c.ColorHex, c.Keywords))
            .ToList();
        theirs[1] = theirs[1] with { Name = "Resto" };

        await service.SetForGroupAsync(ownerId, group.Id, new SetCategoriesRequest(theirs));

        await service.SetGlobalAsync(ownerId, new SetCategoriesRequest(
            [new CategoryInputDto("Everything", "everything")]));

        var after = await service.GetForGroupAsync(ownerId, group.Id);
        after.Select(c => c.Name).ShouldContain("Resto");
        after.Count.ShouldBe(theirs.Count);
    }

    [Fact]
    public async Task Any_member_can_edit_a_groups_list()
    {
        await SeedGlobalAsync();
        var (_, memberId, group) = await AGroupAsync();

        var updated = await NewService().SetForGroupAsync(memberId, group.Id,
            new SetCategoriesRequest([new CategoryInputDto("Ski")]));

        updated.ShouldHaveSingleItem().Key.ShouldBe("ski");
    }

    [Fact]
    public async Task Somebody_outside_the_group_cannot()
    {
        await SeedGlobalAsync();
        var (_, _, group) = await AGroupAsync();
        var stranger = await TestData.SeedUserAsync(Db, "Stranger", "stranger@example.com");

        await Should.ThrowAsync<ForbiddenException>(
            () => NewService().GetForGroupAsync(stranger.Id, group.Id));
        await Should.ThrowAsync<ForbiddenException>(
            () => NewService().SetForGroupAsync(stranger.Id, group.Id,
                new SetCategoriesRequest([new CategoryInputDto("Ski")])));
    }

    [Fact]
    public async Task Only_whoever_runs_the_server_touches_the_servers_list()
    {
        await SeedGlobalAsync();
        var (ownerId, _, _) = await AGroupAsync();

        await Should.ThrowAsync<ForbiddenException>(() => NewService().GetGlobalAsync(ownerId));
        await Should.ThrowAsync<ForbiddenException>(() => NewService().SetGlobalAsync(
            ownerId, new SetCategoriesRequest([new CategoryInputDto("Everything")])));

        var asAdmin = NewService(Administers(ownerId));
        (await asAdmin.GetGlobalAsync(ownerId)).Count.ShouldBe(CategorySeed.Categories.Count);
    }

    [Fact]
    public async Task A_new_category_gets_a_key_from_its_name()
    {
        var (_, memberId, group) = await AGroupAsync();

        var updated = await NewService().SetForGroupAsync(memberId, group.Id,
            new SetCategoriesRequest([new CategoryInputDto("Épicerie du coin")]));

        updated.ShouldHaveSingleItem().Key.ShouldBe("epicerie-du-coin");
    }

    [Fact]
    public async Task Renaming_keeps_the_key_the_expenses_are_filed_under()
    {
        var (_, memberId, group) = await AGroupAsync();
        await NewService().SetForGroupAsync(memberId, group.Id,
            new SetCategoriesRequest([new CategoryInputDto("Dining out", "dining")]));

        var updated = await NewService().SetForGroupAsync(memberId, group.Id,
            new SetCategoriesRequest([new CategoryInputDto("Resto", "dining")]));

        updated.ShouldHaveSingleItem().Key.ShouldBe("dining");
        updated[0].Name.ShouldBe("Resto");
    }

    [Fact]
    public async Task The_order_they_are_sent_in_is_the_order_they_come_back_in()
    {
        var (_, memberId, group) = await AGroupAsync();

        var updated = await NewService().SetForGroupAsync(memberId, group.Id,
            new SetCategoriesRequest([
                new CategoryInputDto("Zebra"),
                new CategoryInputDto("Apple"),
                new CategoryInputDto("Moose"),
            ]));

        updated.Select(c => c.Name).ShouldBe(["Zebra", "Apple", "Moose"]);
    }

    [Fact]
    public async Task Two_of_the_same_are_refused_rather_than_silently_merged()
    {
        var (_, memberId, group) = await AGroupAsync();

        await Should.ThrowAsync<ValidationException>(
            () => NewService().SetForGroupAsync(memberId, group.Id, new SetCategoriesRequest([
                new CategoryInputDto("Ski"),
                new CategoryInputDto("ski"),
            ])));
    }

    [Fact]
    public async Task Keywords_are_tidied_and_bounded()
    {
        var (_, memberId, group) = await AGroupAsync();

        var updated = await NewService().SetForGroupAsync(memberId, group.Id,
            new SetCategoriesRequest([
                new CategoryInputDto("Groceries", Keywords: ["  METRO ", "metro", "", "IGA"]),
            ]));

        updated[0].Keywords.ShouldBe(["metro", "iga"]);

        var many = Enumerable.Range(0, 41).Select(index => $"word{index}").ToList();
        await Should.ThrowAsync<ValidationException>(
            () => NewService().SetForGroupAsync(memberId, group.Id,
                new SetCategoriesRequest([new CategoryInputDto("Groceries", Keywords: many)])));
    }

    [Fact]
    public async Task A_list_of_them_is_bounded()
    {
        var (_, memberId, group) = await AGroupAsync();
        var many = Enumerable.Range(0, 31)
            .Select(index => new CategoryInputDto($"Category {index}"))
            .ToList();

        await Should.ThrowAsync<ValidationException>(
            () => NewService().SetForGroupAsync(memberId, group.Id, new SetCategoriesRequest(many)));
    }

    [Fact]
    public async Task An_empty_list_puts_a_group_back_on_the_servers()
    {
        await SeedGlobalAsync();
        var (_, memberId, group) = await AGroupAsync();
        await NewService().SetForGroupAsync(memberId, group.Id,
            new SetCategoriesRequest([new CategoryInputDto("Ski")]));

        var back = await NewService().SetForGroupAsync(memberId, group.Id, new SetCategoriesRequest([]));

        back.Count.ShouldBe(CategorySeed.Categories.Count);
        (await Db.Categories.CountAsync(c => c.GroupId == group.Id)).ShouldBe(0);
    }

    [Fact]
    public async Task A_deleted_group_takes_its_categories_with_it()
    {
        var (ownerId, memberId, group) = await AGroupAsync();
        await NewService().SetForGroupAsync(memberId, group.Id,
            new SetCategoriesRequest([new CategoryInputDto("Ski")]));

        await Groups.ArchiveAsync(ownerId, group.Id);
        await Db.Groups.Where(g => g.Id == group.Id).ExecuteDeleteAsync();

        (await Db.Categories.CountAsync(c => c.GroupId == group.Id)).ShouldBe(0);
    }
}
