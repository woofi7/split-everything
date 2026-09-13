using System.Net;
using System.Net.Http.Json;
using Shouldly;
using SplitEverything.Application.Contracts.Admin;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Tests.Support;

namespace SplitEverything.Tests.Api;

/// <summary>
/// The endpoints for whoever runs the server.
///
/// Configuration says who that is, so these run against a host where one address
/// administers and every other account - signed in perfectly legitimately - is
/// refused. That refusal is the point of most of what follows: this is the one
/// route in the application that reads across groups and the only one that destroys
/// anything.
/// </summary>
public class AdminEndpointTests(PostgresFixture fixture) : ApiTestBase(fixture)
{
    private async Task<GroupDto> AGroupSomebodyElseOwnsAsync()
    {
        var other = await SignInAsAnotherUserAsync("Emma");

        var response = await other.PostAsJsonAsync("/api/groups",
            new CreateGroupRequest("Their flat", "CAD", null, null, null, ["Roommate"]), Json);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<GroupDto>(Json))!;
    }

    [Fact]
    public async Task An_administrator_sees_every_group_including_the_ones_they_are_not_in()
    {
        var theirs = await AGroupSomebodyElseOwnsAsync();
        await SignInAsync("Admin", "admin@example.com");

        var groups = await Client.GetFromJsonAsync<List<AdminGroupDto>>("/api/admin/groups", Json);

        groups!.ShouldHaveSingleItem().Id.ShouldBe(theirs.Id);
        groups[0].IsMine.ShouldBeFalse();
    }

    [Fact]
    public async Task An_administrator_can_read_one_without_joining_it()
    {
        var theirs = await AGroupSomebodyElseOwnsAsync();
        await SignInAsync("Admin", "admin@example.com");

        var detail = await Client.GetFromJsonAsync<AdminGroupDetailDto>(
            $"/api/admin/groups/{theirs.Id}", Json);

        detail!.Members.Count.ShouldBe(2);
        detail.Group.MemberCount.ShouldBe(2);
    }

    [Fact]
    public async Task Everybody_else_is_refused()
    {
        var theirs = await AGroupSomebodyElseOwnsAsync();

        // Signed in, and the owner of that group: this route is still not theirs.
        await SignInAsync("Emma", "emma@example.com");

        (await Client.GetAsync("/api/admin/groups")).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
        (await Client.GetAsync($"/api/admin/groups/{theirs.Id}")).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
        (await Client.DeleteAsync($"/api/admin/groups/{theirs.Id}")).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Nobody_at_all_is_refused_before_any_of_that()
    {
        var anonymous = Factory.CreateClient();

        (await anonymous.GetAsync("/api/admin/groups")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_archived_group_can_be_deleted_and_one_in_use_cannot()
    {
        var theirs = await AGroupSomebodyElseOwnsAsync();
        await SignInAsync("Admin", "admin@example.com");

        // Archiving is the reversible step, and it comes first.
        (await Client.DeleteAsync($"/api/admin/groups/{theirs.Id}")).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);

        var owner = await SignInAsAnotherUserAsync("Emma");
        (await owner.PostAsync($"/api/groups/{theirs.Id}/archive", null)).EnsureSuccessStatusCode();

        (await Client.DeleteAsync($"/api/admin/groups/{theirs.Id}")).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);

        var left = await Client.GetFromJsonAsync<List<AdminGroupDto>>("/api/admin/groups", Json);
        left!.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_group_that_is_not_there_is_a_404()
    {
        await SignInAsync("Admin", "admin@example.com");

        (await Client.GetAsync($"/api/admin/groups/{Guid.NewGuid()}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await Client.DeleteAsync($"/api/admin/groups/{Guid.NewGuid()}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The one group setting that is not an owner's or an admin's: it changes what
    /// a total reads and nothing about what anybody owes.
    /// </summary>
    [Fact]
    public async Task Any_member_can_set_the_names_left_out_of_the_totals()
    {
        var mine = await SignInAsync("Alice");

        var created = await Client.PostAsJsonAsync("/api/groups",
            new CreateGroupRequest("Roommates", "CAD", null, null, null, null), Json);
        var group = (await created.Content.ReadFromJsonAsync<GroupDto>(Json))!;

        var emma = await SignInAsAnotherUserAsync("Emma");
        var users = await Client.GetFromJsonAsync<List<AddableUserDto>>(
            $"/api/users/addable?groupId={group.Id}", Json);
        var emmaId = users!.Single(candidate => candidate.Id != mine.Id).Id;

        (await Client.PostAsJsonAsync($"/api/groups/{group.Id}/members/user",
            new AddUserMemberRequest(emmaId), Json)).EnsureSuccessStatusCode();

        var response = await emma.PutAsJsonAsync($"/api/groups/{group.Id}/ignored-names",
            new SetIgnoredNamesRequest(["Loyer"]), Json);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = (await response.Content.ReadFromJsonAsync<GroupDto>(Json))!;
        updated.IgnoredNamePatterns.ShouldBe(["Loyer"]);
    }
}
