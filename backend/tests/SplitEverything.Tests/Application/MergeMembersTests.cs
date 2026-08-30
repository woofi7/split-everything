using Microsoft.EntityFrameworkCore;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Expenses;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Application.Contracts.Settlements;
using SplitEverything.Domain.Common;
using SplitEverything.Domain.Entities;
using SplitEverything.Tests.Support;
using Shouldly;

namespace SplitEverything.Tests.Application;

/// <summary>
/// Folding one member into another.
///
/// The same person lands in a group twice: once as a name a CSV import invented
/// from an export, and again as the account they later signed up with. Both halves
/// carry expenses, so neither can simply be deleted.
///
/// The dangerous cases are the collisions. A split and an item share are each
/// keyed by member and unique per expense, so an expense both halves were part of
/// has two rows that have to become one. Getting that wrong either throws on the
/// unique index or silently loses half of what someone owed.
/// </summary>
public class MergeMembersTests(PostgresFixture fixture) : ServiceTestBase(fixture)
{
    private async Task<(Guid UserId, GroupDto Group, Guid Alice, Guid Bob, Guid Ghost)> SetupAsync()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, ["Bob", "Bobby"]));

        return (user.Id, group,
            group.Members.First(m => m.UserId == user.Id).Id,
            group.Members.First(m => m.DisplayName == "Bob").Id,
            group.Members.First(m => m.DisplayName == "Bobby").Id);
    }

    private static CreateExpenseRequest Expense(
        Guid groupId, Guid payer, decimal amount, params Guid[] participants)
        => new(groupId, payer, "Dinner", amount, "CAD", TestData.Jan1, SplitType.Equal,
            participants.Select(p => new SplitInputDto(p, null)).ToList(),
            null, null, null, null, null, null);

    private static MergeMembersRequest Merge(Guid source, Guid target) => new(source, target);

    [Fact]
    public async Task The_source_is_gone_from_the_group_afterwards()
    {
        var (userId, group, alice, bob, ghost) = await SetupAsync();

        var merged = await Groups.MergeMembersAsync(userId, group.Id, Merge(ghost, bob));

        merged.Members.ShouldNotContain(m => m.Id == ghost && m.Status == MembershipStatus.Active);
        merged.Members.ShouldContain(m => m.Id == bob);
        merged.Members.ShouldContain(m => m.Id == alice);
    }

    [Fact]
    public async Task What_the_source_paid_is_now_paid_by_the_target()
    {
        var (userId, group, alice, bob, ghost) = await SetupAsync();
        var expense = await Expenses.CreateAsync(userId, Expense(group.Id, ghost, 30m, alice, ghost));

        await Groups.MergeMembersAsync(userId, group.Id, Merge(ghost, bob));

        var fresh = NewContext();
        (await fresh.Expenses.FirstAsync(e => e.Id == expense.Id)).PaidByMemberId.ShouldBe(bob);
    }

    [Fact]
    public async Task What_the_source_owed_is_now_owed_by_the_target()
    {
        var (userId, group, alice, bob, ghost) = await SetupAsync();
        var expense = await Expenses.CreateAsync(userId, Expense(group.Id, alice, 30m, alice, ghost));

        await Groups.MergeMembersAsync(userId, group.Id, Merge(ghost, bob));

        var fresh = NewContext();
        var splits = await fresh.ExpenseSplits.Where(s => s.ExpenseId == expense.Id).ToListAsync();
        splits.ShouldNotContain(s => s.MemberId == ghost);
        splits.First(s => s.MemberId == bob).Amount.ShouldBe(15m);
    }

    [Fact]
    public async Task Two_shares_of_one_expense_become_one()
    {
        var (userId, group, alice, bob, ghost) = await SetupAsync();
        // Both halves of the same person were on this: 10 each of 30.
        var expense = await Expenses.CreateAsync(userId, Expense(group.Id, alice, 30m, alice, bob, ghost));

        await Groups.MergeMembersAsync(userId, group.Id, Merge(ghost, bob));

        var fresh = NewContext();
        var splits = await fresh.ExpenseSplits.Where(s => s.ExpenseId == expense.Id).ToListAsync();

        // One row, holding both halves. Two rows would break the unique index;
        // one row holding only half would quietly lose 10 from the balance.
        splits.Count(s => s.MemberId == bob).ShouldBe(1);
        splits.First(s => s.MemberId == bob).Amount.ShouldBe(20m);
    }

    [Fact]
    public async Task The_expense_still_adds_up_to_what_was_spent()
    {
        var (userId, group, alice, bob, ghost) = await SetupAsync();
        var expense = await Expenses.CreateAsync(userId, Expense(group.Id, alice, 30m, alice, bob, ghost));

        await Groups.MergeMembersAsync(userId, group.Id, Merge(ghost, bob));

        var fresh = NewContext();
        var splits = await fresh.ExpenseSplits.Where(s => s.ExpenseId == expense.Id).ToListAsync();
        splits.Sum(s => s.Amount).ShouldBe(30m);
    }

    [Fact]
    public async Task A_settlement_between_the_two_halves_is_dropped()
    {
        var (userId, group, _, bob, ghost) = await SetupAsync();
        var settlement = await Settlements.CreateAsync(userId,
            new CreateSettlementRequest(group.Id, ghost, bob, 10m, "CAD", TestData.Jan1, null, null, null));

        await Groups.MergeMembersAsync(userId, group.Id, Merge(ghost, bob));

        // A payment from someone to themselves says nothing. It only meant
        // anything while the two were different people.
        var fresh = NewContext();
        (await fresh.Settlements.FirstAsync(s => s.Id == settlement.Id)).IsDeleted.ShouldBeTrue();
    }

    [Fact]
    public async Task A_settlement_with_someone_else_is_repointed()
    {
        var (userId, group, alice, bob, ghost) = await SetupAsync();
        var settlement = await Settlements.CreateAsync(userId,
            new CreateSettlementRequest(group.Id, ghost, alice, 10m, "CAD", TestData.Jan1, null, null, null));

        await Groups.MergeMembersAsync(userId, group.Id, Merge(ghost, bob));

        var fresh = NewContext();
        var stored = await fresh.Settlements.FirstAsync(s => s.Id == settlement.Id);
        stored.FromMemberId.ShouldBe(bob);
        stored.IsDeleted.ShouldBeFalse();
    }

    [Fact]
    public async Task Comments_keep_an_author_who_still_exists()
    {
        var (userId, group, alice, bob, ghost) = await SetupAsync();
        var expense = await Expenses.CreateAsync(userId, Expense(group.Id, alice, 30m, alice, ghost));

        var fresh = NewContext();
        fresh.ExpenseComments.Add(new ExpenseComment
        {
            ExpenseId = expense.Id,
            GroupId = group.Id,
            AuthorMemberId = ghost,
            Body = "I paid this one"
        });
        await fresh.SaveChangesAsync();

        await Groups.MergeMembersAsync(userId, group.Id, Merge(ghost, bob));

        var after = NewContext();
        (await after.ExpenseComments.FirstAsync(c => c.ExpenseId == expense.Id))
            .AuthorMemberId.ShouldBe(bob);
    }

    [Fact]
    public async Task Every_moved_expense_is_offered_to_the_other_devices()
    {
        var (userId, group, alice, bob, ghost) = await SetupAsync();
        var expense = await Expenses.CreateAsync(userId, Expense(group.Id, ghost, 30m, alice, ghost));

        var before = await NewContext().SyncLog.CountAsync(e => e.EntityId == expense.Id);
        await Groups.MergeMembersAsync(userId, group.Id, Merge(ghost, bob));

        // Otherwise a phone that already has this expense keeps showing the old
        // payer for good: a pull only sends what changed since its cursor.
        var after = await NewContext().SyncLog.CountAsync(e => e.EntityId == expense.Id);
        after.ShouldBeGreaterThan(before);
    }

    [Fact]
    public async Task The_group_default_split_follows_the_merge()
    {
        var (userId, group, alice, bob, ghost) = await SetupAsync();
        await Groups.UpdateAsync(userId, group.Id, new UpdateGroupRequest(
            null, null, null, null, null, SplitType.Shares,
            new Dictionary<Guid, decimal> { [alice] = 1m, [bob] = 2m, [ghost] = 3m }));

        var merged = await Groups.MergeMembersAsync(userId, group.Id, Merge(ghost, bob));

        // A default keyed by a member who no longer exists would silently drop that
        // share out of every future expense.
        var values = merged.DefaultSplitValues.ShouldNotBeNull();
        values.ShouldNotContainKey(ghost);
        values[bob].ShouldBe(5m);
    }

    [Fact]
    public async Task The_merge_is_recorded_in_the_activity_feed()
    {
        var (userId, group, _, bob, ghost) = await SetupAsync();

        await Groups.MergeMembersAsync(userId, group.Id, Merge(ghost, bob));

        var fresh = NewContext();
        var entry = await fresh.ActivityLog
            .Where(a => a.GroupId == group.Id && a.Kind == ActivityKind.MembersMerged)
            .FirstOrDefaultAsync();

        entry.ShouldNotBeNull();
        entry.Summary.ShouldContain("Bobby");
        entry.Summary.ShouldContain("Bob");
    }

    [Fact]
    public async Task Merging_someone_into_themselves_is_refused()
    {
        var (userId, group, _, bob, _) = await SetupAsync();

        await Should.ThrowAsync<ValidationException>(
            () => Groups.MergeMembersAsync(userId, group.Id, Merge(bob, bob)));
    }

    [Fact]
    public async Task The_owner_cannot_be_merged_away()
    {
        var (userId, group, alice, bob, _) = await SetupAsync();

        // A group has to keep an owner, and the surviving member may not have an
        // account at all. Merging the other way round does the same job.
        await Should.ThrowAsync<ValidationException>(
            () => Groups.MergeMembersAsync(userId, group.Id, Merge(alice, bob)));
    }

    [Fact]
    public async Task Someone_who_is_not_a_member_cannot_be_merged()
    {
        var (userId, group, _, bob, _) = await SetupAsync();

        await Should.ThrowAsync<NotFoundException>(
            () => Groups.MergeMembersAsync(userId, group.Id, Merge(Guid.CreateVersion7(), bob)));
    }

    [Fact]
    public async Task Only_an_admin_can_merge()
    {
        var (_, group, _, bob, ghost) = await SetupAsync();
        var other = await TestData.SeedUserAsync(Db, "Mallory", "mallory@example.com", "google-mallory");

        var fresh = NewContext();
        fresh.GroupMembers.Add(TestData.Member(group.Id, other.Id, "Mallory"));
        await fresh.SaveChangesAsync();

        // It rewrites everyone's balances, so it is not a thing any member can do.
        await Should.ThrowAsync<ForbiddenException>(
            () => Groups.MergeMembersAsync(other.Id, group.Id, Merge(ghost, bob)));
    }

    [Fact]
    public async Task An_archived_group_cannot_be_merged_into()
    {
        var (userId, group, _, bob, ghost) = await SetupAsync();
        await Groups.ArchiveAsync(userId, group.Id);

        await Should.ThrowAsync<GroupArchivedException>(
            () => Groups.MergeMembersAsync(userId, group.Id, Merge(ghost, bob)));
    }
}
