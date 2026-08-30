using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Expenses;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Domain.Common;
using SplitEverything.Infrastructure.Services;
using SplitEverything.Tests.Support;
using Shouldly;

namespace SplitEverything.Tests.Application;

/// <summary>
/// The actor on an activity entry.
///
/// The app colours people by member id, and the feed only carried the account id.
/// The same person would have had one colour on an expense card and another beside
/// the entry about it, which is worse than no colour at all.
/// </summary>
public class ActivityActorTests(PostgresFixture fixture) : ServiceTestBase(fixture)
{
    private async Task<(Guid UserId, GroupDto Group, Guid Me)> SetupAsync()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, ["Bob"]));
        return (user.Id, group, group.Members.First(m => m.UserId == user.Id).Id);
    }

    [Fact]
    public async Task An_entry_names_the_membership_that_acted()
    {
        var (userId, group, me) = await SetupAsync();

        await Expenses.CreateAsync(userId, new CreateExpenseRequest(
            group.Id, me, "Dinner", 40m, "CAD", TestData.Jan1, SplitType.Equal,
            [new SplitInputDto(me, null)], null, null, null, null, null, null, null));

        var feed = await Activity.ListAsync(userId, group.Id, new PageRequest(1, 20));

        var entry = feed.Items.First(e => e.Kind == ActivityKind.ExpenseCreated);
        entry.ActorMemberId.ShouldBe(me);
        entry.ActorUserId.ShouldBe(userId);
    }

    [Fact]
    public async Task The_member_id_matches_the_one_on_the_expense()
    {
        var (userId, group, me) = await SetupAsync();

        var expense = await Expenses.CreateAsync(userId, new CreateExpenseRequest(
            group.Id, me, "Dinner", 40m, "CAD", TestData.Jan1, SplitType.Equal,
            [new SplitInputDto(me, null)], null, null, null, null, null, null, null));

        var feed = await Activity.ListAsync(userId, group.Id, new PageRequest(1, 20));
        var entry = feed.Items.First(e => e.SubjectId == expense.Id);

        // Same key, so the same colour on both screens.
        entry.ActorMemberId.ShouldBe(expense.PaidByMemberId);
    }

    [Fact]
    public async Task An_entry_with_no_actor_still_comes_back()
    {
        var (userId, group, _) = await SetupAsync();

        await Activity.RecordAsync(group.Id, ActivityKind.GroupCreated, null, null,
            null, null, "Something happened");
        await Db.SaveChangesAsync();

        var feed = await Activity.ListAsync(userId, group.Id, new PageRequest(1, 20));

        // A system entry has nobody to colour, and must not be dropped for it.
        feed.Items.ShouldContain(e => e.Summary == "Something happened" && e.ActorMemberId == null);
    }
}
