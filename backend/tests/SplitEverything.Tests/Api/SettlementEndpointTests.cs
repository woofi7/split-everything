using System.Net;
using System.Net.Http.Json;
using Shouldly;
using SplitEverything.Application.Contracts.Expenses;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Application.Contracts.Settlements;
using SplitEverything.Domain.Common;
using SplitEverything.Tests.Support;

namespace SplitEverything.Tests.Api;

public class SettlementEndpointTests(PostgresFixture fixture) : ApiTestBase(fixture)
{
    private async Task<(Guid MeId, Guid EmmaUserId, GroupDto Group)> ADebtAsync(
        string groupName = "Roommates", decimal amount = 60m)
    {
        var me = await SignInAsync();

        var emma = await SignInAsAnotherUserAsync("Emma");
        (await emma.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();

        var created = await Client.PostAsJsonAsync("/api/groups",
            new CreateGroupRequest(groupName, "CAD", null, null, null, null), Json);
        created.EnsureSuccessStatusCode();
        var group = (await created.Content.ReadFromJsonAsync<GroupDto>(Json))!;

        var addable = await Client.GetFromJsonAsync<List<AddableUserDto>>(
            $"/api/users/addable?groupId={group.Id}", Json);
        var emmaUserId = addable!.Single(candidate => candidate.Id != me.Id).Id;

        (await Client.PostAsJsonAsync($"/api/groups/{group.Id}/members/user",
            new AddUserMemberRequest(emmaUserId), Json)).EnsureSuccessStatusCode();

        var withEmma = (await Client.GetFromJsonAsync<GroupDto>($"/api/groups/{group.Id}", Json))!;
        var mine = withEmma.Members.Single(member => member.UserId == me.Id).Id;

        (await Client.PostAsJsonAsync("/api/expenses",
            new CreateExpenseRequest(
                group.Id, mine, "Groceries", amount, "CAD", DateTimeOffset.UtcNow,
                SplitType.Equal,
                withEmma.Members.Select(member => new SplitInputDto(member.Id, null)).ToList(),
                null, null, null, null, null, null), Json)).EnsureSuccessStatusCode();

        return (me.Id, emmaUserId, withEmma);
    }

    [Fact]
    public async Task A_nudge_is_accepted_and_says_nothing_back()
    {
        var (_, emmaUserId, group) = await ADebtAsync();
        var hers = group.Members.Single(member => member.UserId == emmaUserId).Id;

        var response = await Client.PostAsJsonAsync("/api/settlements/nudge",
            new NudgeRequest(group.Id, hers, "When you get a chance"), Json);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_member_colour_can_be_set_through_the_api()
    {
        var me = await SignInAsync();

        var created = await Client.PostAsJsonAsync("/api/groups",
            new CreateGroupRequest("Roommates", "CAD", null, null, null, null), Json);
        var group = (await created.Content.ReadFromJsonAsync<GroupDto>(Json))!;
        var mine = group.Members.Single(member => member.UserId == me.Id).Id;

        var response = await Client.PatchAsJsonAsync(
            $"/api/groups/{group.Id}/members/{mine}/color",
            new SetMemberColorRequest(MemberPalette.Colors[3]), Json);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var member = (await response.Content.ReadFromJsonAsync<GroupMemberDto>(Json))!;
        member.ColorHex.ShouldBe(MemberPalette.Colors[3]);
    }
}
