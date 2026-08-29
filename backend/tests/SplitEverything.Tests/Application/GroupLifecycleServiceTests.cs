using Microsoft.EntityFrameworkCore;
using Shouldly;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Expenses;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Domain.Common;
using SplitEverything.Infrastructure.Services;
using SplitEverything.Tests.Support;

namespace SplitEverything.Tests.Application;

/// <summary>
/// Merge, split, transfer and compaction: the operations that move history between
/// logs. The invariant under test throughout is that no expense, revision, comment
/// or log entry is ever recreated - it moves, keeping its causal identity.
/// </summary>
public class GroupLifecycleServiceTests(PostgresFixture fixture) : ServiceTestBase(fixture)
{
    private GroupLifecycleService Lifecycle => new(Db, Writer, Activity, Clock);

    private async Task<(GroupDto Group, Guid OwnerMember, Guid Other)> MakeGroupAsync(
        Guid userId, string name, string otherName)
    {
        var group = await Groups.CreateAsync(userId,
            new CreateGroupRequest(name, "CAD", null, null, null, [otherName]));
        return (group,
            group.Members.First(m => m.UserId == userId).Id,
            group.Members.First(m => m.DisplayName == otherName).Id);
    }

    private Task<ExpenseDto> AddExpenseAsync(
        Guid userId, Guid groupId, Guid payer, decimal amount, string description, params Guid[] participants)
        => Expenses.CreateAsync(userId, new CreateExpenseRequest(
            groupId, payer, description, amount, "CAD", TestData.Jan1, SplitType.Equal,
            participants.Select(p => new SplitInputDto(p, null)).ToList(),
            null, null, null, null, null, null, null));

    // ---- merge -----------------------------------------------------------

    [Fact]
    public async Task Merging_moves_the_source_expenses_into_the_target()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (target, targetMe, targetBob) = await MakeGroupAsync(user.Id, "Keep", "Bob");
        var (source, sourceMe, sourceBob) = await MakeGroupAsync(user.Id, "Fold in", "Bob");
        await AddExpenseAsync(user.Id, source.Id, sourceMe, 60m, "From source", sourceMe, sourceBob);

        var result = await Lifecycle.MergeAsync(user.Id, new MergeGroupsRequest(
            source.Id, target.Id,
            new Dictionary<Guid, Guid> { [sourceMe] = targetMe, [sourceBob] = targetBob }, null));

        result.MovedExpenses.ShouldBe(1);
        var moved = await NewContext().Expenses.SingleAsync(e => e.Description == "From source");
        moved.GroupId.ShouldBe(target.Id);
    }

    [Fact]
    public async Task Merging_remaps_the_splits_onto_the_target_members()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (target, targetMe, targetBob) = await MakeGroupAsync(user.Id, "Keep", "Bob");
        var (source, sourceMe, sourceBob) = await MakeGroupAsync(user.Id, "Fold in", "Bob");
        var expense = await AddExpenseAsync(user.Id, source.Id, sourceMe, 60m, "From source", sourceMe, sourceBob);

        await Lifecycle.MergeAsync(user.Id, new MergeGroupsRequest(
            source.Id, target.Id,
            new Dictionary<Guid, Guid> { [sourceMe] = targetMe, [sourceBob] = targetBob }, null));

        var splits = await NewContext().ExpenseSplits.Where(s => s.ExpenseId == expense.Id).ToListAsync();
        splits.Select(s => s.MemberId).ShouldBe(new[] { targetMe, targetBob }, ignoreOrder: true);
        splits.ShouldAllBe(s => s.GroupId == target.Id);
    }

    [Fact]
    public async Task Merging_keeps_the_balances_the_two_groups_had_between_them()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (target, targetMe, targetBob) = await MakeGroupAsync(user.Id, "Keep", "Bob");
        var (source, sourceMe, sourceBob) = await MakeGroupAsync(user.Id, "Fold in", "Bob");
        await AddExpenseAsync(user.Id, target.Id, targetMe, 40m, "In target", targetMe, targetBob);
        await AddExpenseAsync(user.Id, source.Id, sourceMe, 60m, "In source", sourceMe, sourceBob);

        await Lifecycle.MergeAsync(user.Id, new MergeGroupsRequest(
            source.Id, target.Id,
            new Dictionary<Guid, Guid> { [sourceMe] = targetMe, [sourceBob] = targetBob }, null));

        // I fronted 100 across both groups and owe half of it, so I am up 50.
        var balance = await Settlements.GetGroupBalanceAsync(user.Id, target.Id);
        balance.Balances.First(b => b.MemberId == targetMe).Net.ShouldBe(50m);
        balance.Balances.Sum(b => b.Net).ShouldBe(0m);
    }

    [Fact]
    public async Task Merging_moves_the_source_log_entries_rather_than_replaying_them()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (target, targetMe, targetBob) = await MakeGroupAsync(user.Id, "Keep", "Bob");
        var (source, sourceMe, sourceBob) = await MakeGroupAsync(user.Id, "Fold in", "Bob");
        await AddExpenseAsync(user.Id, source.Id, sourceMe, 60m, "From source", sourceMe, sourceBob);
        var sourceEntries = await Db.SyncLog.CountAsync(e => e.GroupId == source.Id);

        var result = await Lifecycle.MergeAsync(user.Id, new MergeGroupsRequest(
            source.Id, target.Id,
            new Dictionary<Guid, Guid> { [sourceMe] = targetMe, [sourceBob] = targetBob }, null));

        result.MovedLogEntries.ShouldBe(sourceEntries);
        (await NewContext().SyncLog.CountAsync(e => e.GroupId == source.Id)).ShouldBe(0);
    }

    [Fact]
    public async Task Merged_log_entries_keep_their_original_lineage()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (target, targetMe, targetBob) = await MakeGroupAsync(user.Id, "Keep", "Bob");
        var (source, sourceMe, sourceBob) = await MakeGroupAsync(user.Id, "Fold in", "Bob");
        await AddExpenseAsync(user.Id, source.Id, sourceMe, 60m, "From source", sourceMe, sourceBob);
        var sourceLineage = (await Db.Groups.FirstAsync(g => g.Id == source.Id)).LineageId;

        await Lifecycle.MergeAsync(user.Id, new MergeGroupsRequest(
            source.Id, target.Id,
            new Dictionary<Guid, Guid> { [sourceMe] = targetMe, [sourceBob] = targetBob }, null));

        // Keeping the lineage is what makes a later split able to partition the
        // merged log again instead of guessing which side an entry came from.
        (await NewContext().SyncLog.CountAsync(e =>
            e.GroupId == target.Id && e.LineageId == sourceLineage)).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Merged_log_entries_are_renumbered_without_colliding()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (target, targetMe, targetBob) = await MakeGroupAsync(user.Id, "Keep", "Bob");
        var (source, sourceMe, sourceBob) = await MakeGroupAsync(user.Id, "Fold in", "Bob");
        await AddExpenseAsync(user.Id, target.Id, targetMe, 40m, "In target", targetMe, targetBob);
        await AddExpenseAsync(user.Id, source.Id, sourceMe, 60m, "In source", sourceMe, sourceBob);

        await Lifecycle.MergeAsync(user.Id, new MergeGroupsRequest(
            source.Id, target.Id,
            new Dictionary<Guid, Guid> { [sourceMe] = targetMe, [sourceBob] = targetBob }, null));

        var sequences = await NewContext().SyncLog
            .Where(e => e.GroupId == target.Id)
            .Select(e => e.ServerSeq)
            .ToListAsync();

        sequences.Distinct().Count().ShouldBe(sequences.Count);
    }

    [Fact]
    public async Task Merged_entries_are_renumbered_above_everything_the_target_already_had()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (target, targetMe, targetBob) = await MakeGroupAsync(user.Id, "Keep", "Bob");
        var (source, sourceMe, sourceBob) = await MakeGroupAsync(user.Id, "Fold in", "Bob");
        await AddExpenseAsync(user.Id, target.Id, targetMe, 40m, "In target", targetMe, targetBob);
        var targetHigh = await Db.SyncLog.Where(e => e.GroupId == target.Id).MaxAsync(e => e.ServerSeq);
        await AddExpenseAsync(user.Id, source.Id, sourceMe, 60m, "In source", sourceMe, sourceBob);
        var sourceLineage = (await Db.Groups.FirstAsync(g => g.Id == source.Id)).LineageId;

        await Lifecycle.MergeAsync(user.Id, new MergeGroupsRequest(
            source.Id, target.Id,
            new Dictionary<Guid, Guid> { [sourceMe] = targetMe, [sourceBob] = targetBob }, null));

        // A device following the target pulls "everything after N", so moved history
        // has to land above its cursor or it would never be delivered.
        var movedSequences = await NewContext().SyncLog
            .Where(e => e.GroupId == target.Id && e.LineageId == sourceLineage)
            .Select(e => e.ServerSeq)
            .ToListAsync();

        movedSequences.ShouldAllBe(seq => seq > targetHigh);
    }

    [Fact]
    public async Task Merging_writes_a_marker_entry_in_the_target_log()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (target, targetMe, targetBob) = await MakeGroupAsync(user.Id, "Keep", "Bob");
        var (source, sourceMe, sourceBob) = await MakeGroupAsync(user.Id, "Fold in", "Bob");

        await Lifecycle.MergeAsync(user.Id, new MergeGroupsRequest(
            source.Id, target.Id,
            new Dictionary<Guid, Guid> { [sourceMe] = targetMe, [sourceBob] = targetBob }, null));

        var marker = await NewContext().SyncLog
            .SingleAsync(e => e.GroupId == target.Id && e.Operation == SyncOperation.Merge);
        marker.CounterpartGroupId.ShouldBe(source.Id);
    }

    [Fact]
    public async Task Merging_joins_the_two_clocks_on_the_target()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (target, targetMe, targetBob) = await MakeGroupAsync(user.Id, "Keep", "Bob");
        var (source, sourceMe, sourceBob) = await MakeGroupAsync(user.Id, "Fold in", "Bob");
        var sourceClock = (await Db.Groups.FirstAsync(g => g.Id == source.Id)).Clock;

        await Lifecycle.MergeAsync(user.Id, new MergeGroupsRequest(
            source.Id, target.Id,
            new Dictionary<Guid, Guid> { [sourceMe] = targetMe, [sourceBob] = targetBob }, null));

        (await NewContext().Groups.FirstAsync(g => g.Id == target.Id))
            .Clock.Dominates(sourceClock).ShouldBeTrue();
    }

    [Fact]
    public async Task Merging_archives_the_source_rather_than_deleting_it()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (target, targetMe, targetBob) = await MakeGroupAsync(user.Id, "Keep", "Bob");
        var (source, sourceMe, sourceBob) = await MakeGroupAsync(user.Id, "Fold in", "Bob");

        await Lifecycle.MergeAsync(user.Id, new MergeGroupsRequest(
            source.Id, target.Id,
            new Dictionary<Guid, Guid> { [sourceMe] = targetMe, [sourceBob] = targetBob }, null));

        (await NewContext().Groups.FirstAsync(g => g.Id == source.Id)).IsArchived.ShouldBeTrue();
    }

    [Fact]
    public async Task Merging_records_a_lineage_link()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (target, targetMe, targetBob) = await MakeGroupAsync(user.Id, "Keep", "Bob");
        var (source, sourceMe, sourceBob) = await MakeGroupAsync(user.Id, "Fold in", "Bob");

        var result = await Lifecycle.MergeAsync(user.Id, new MergeGroupsRequest(
            source.Id, target.Id,
            new Dictionary<Guid, Guid> { [sourceMe] = targetMe, [sourceBob] = targetBob }, "Same roommates"));

        var link = await NewContext().GroupLineageLinks.SingleAsync(l => l.Id == result.LineageLinkId);
        link.Kind.ShouldBe(GroupLineageKind.Merge);
        link.SourceGroupId.ShouldBe(source.Id);
        link.TargetGroupId.ShouldBe(target.Id);
        link.Note.ShouldBe("Same roommates");
    }

    [Fact]
    public async Task Merging_without_a_mapping_matches_members_by_name()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (target, _, targetBob) = await MakeGroupAsync(user.Id, "Keep", "Bob");
        var (source, sourceMe, sourceBob) = await MakeGroupAsync(user.Id, "Fold in", "Bob");
        var expense = await AddExpenseAsync(user.Id, source.Id, sourceMe, 60m, "From source", sourceMe, sourceBob);

        await Lifecycle.MergeAsync(user.Id, new MergeGroupsRequest(source.Id, target.Id, null, null));

        var splits = await NewContext().ExpenseSplits.Where(s => s.ExpenseId == expense.Id).ToListAsync();
        splits.Select(s => s.MemberId).ShouldContain(targetBob);
    }

    [Fact]
    public async Task An_unmapped_source_member_is_carried_over_as_a_new_member()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (target, _, _) = await MakeGroupAsync(user.Id, "Keep", "Bob");
        var (source, sourceMe, sourceZoe) = await MakeGroupAsync(user.Id, "Fold in", "Zoe");
        await AddExpenseAsync(user.Id, source.Id, sourceZoe, 20m, "Zoe paid", sourceMe, sourceZoe);

        await Lifecycle.MergeAsync(user.Id, new MergeGroupsRequest(source.Id, target.Id, null, null));

        var members = await NewContext().GroupMembers
            .Where(m => m.GroupId == target.Id && !m.IsDeleted)
            .Select(m => m.DisplayName)
            .ToListAsync();
        members.ShouldContain("Zoe");
    }

    [Fact]
    public async Task A_group_cannot_be_merged_into_itself()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (group, _, _) = await MakeGroupAsync(user.Id, "Only", "Bob");

        await Should.ThrowAsync<ValidationException>(() => Lifecycle.MergeAsync(
            user.Id, new MergeGroupsRequest(group.Id, group.Id, null, null)));
    }

    [Fact]
    public async Task Merging_needs_admin_rights_on_both_groups()
    {
        var owner = await TestData.SeedUserAsync(Db, "Alice");
        var other = await TestData.SeedUserAsync(Db, "Bob");
        var (target, _, _) = await MakeGroupAsync(owner.Id, "Keep", "Bob");
        var (source, _, _) = await MakeGroupAsync(owner.Id, "Fold in", "Bob");
        Db.GroupMembers.Add(TestData.Member(target.Id, other.Id, "Bob"));
        Db.GroupMembers.Add(TestData.Member(source.Id, other.Id, "Bob"));
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        await Should.ThrowAsync<ForbiddenException>(() => Lifecycle.MergeAsync(
            other.Id, new MergeGroupsRequest(source.Id, target.Id, null, null)));
    }

    [Fact]
    public async Task Groups_with_different_currencies_cannot_be_merged()
    {
        var user = await TestData.SeedUserAsync(Db);
        var target = await Groups.CreateAsync(user.Id, new CreateGroupRequest("CAD group", "CAD", null, null, null, null));
        var source = await Groups.CreateAsync(user.Id, new CreateGroupRequest("EUR group", "EUR", null, null, null, null));

        // Merging across currencies would silently reinterpret every stored base
        // amount, so it is refused rather than guessed at.
        await Should.ThrowAsync<ValidationException>(() => Lifecycle.MergeAsync(
            user.Id, new MergeGroupsRequest(source.Id, target.Id, null, null)));
    }

    [Fact]
    public async Task Merging_moves_settlements_too()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (target, targetMe, targetBob) = await MakeGroupAsync(user.Id, "Keep", "Bob");
        var (source, sourceMe, sourceBob) = await MakeGroupAsync(user.Id, "Fold in", "Bob");
        await Settlements.CreateAsync(user.Id, new SplitEverything.Application.Contracts.Settlements.CreateSettlementRequest(
            source.Id, sourceBob, sourceMe, 15m, "CAD", TestData.Jan1, null, null, null));

        var result = await Lifecycle.MergeAsync(user.Id, new MergeGroupsRequest(
            source.Id, target.Id,
            new Dictionary<Guid, Guid> { [sourceMe] = targetMe, [sourceBob] = targetBob }, null));

        result.MovedSettlements.ShouldBe(1);
        (await NewContext().Settlements.SingleAsync()).GroupId.ShouldBe(target.Id);
    }

    // ---- split -----------------------------------------------------------

    [Fact]
    public async Task Splitting_moves_the_chosen_expenses_into_a_new_group()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (group, me, bob) = await MakeGroupAsync(user.Id, "Everything", "Bob");
        var stays = await AddExpenseAsync(user.Id, group.Id, me, 20m, "Stays", me, bob);
        var moves = await AddExpenseAsync(user.Id, group.Id, me, 30m, "Moves", me, bob);

        var result = await Lifecycle.SplitAsync(user.Id, new SplitGroupRequest(
            group.Id, "Trip", [moves.Id], null, null, null));

        result.MovedExpenses.ShouldBe(1);
        var fresh = NewContext();
        (await fresh.Expenses.FirstAsync(e => e.Id == moves.Id)).GroupId.ShouldBe(result.NewGroupId);
        (await fresh.Expenses.FirstAsync(e => e.Id == stays.Id)).GroupId.ShouldBe(group.Id);
    }

    [Fact]
    public async Task The_new_group_gets_the_members_the_moved_expenses_need()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (group, me, bob) = await MakeGroupAsync(user.Id, "Everything", "Bob");
        var moves = await AddExpenseAsync(user.Id, group.Id, me, 30m, "Moves", me, bob);

        var result = await Lifecycle.SplitAsync(user.Id, new SplitGroupRequest(
            group.Id, "Trip", [moves.Id], null, null, null));

        var members = await NewContext().GroupMembers
            .Where(m => m.GroupId == result.NewGroupId)
            .Select(m => m.DisplayName)
            .ToListAsync();
        members.ShouldBe(new[] { "Alice", "Bob" }, ignoreOrder: true);
    }

    [Fact]
    public async Task Splitting_preserves_the_balances_of_the_moved_expenses()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (group, me, bob) = await MakeGroupAsync(user.Id, "Everything", "Bob");
        var moves = await AddExpenseAsync(user.Id, group.Id, me, 30m, "Moves", me, bob);

        var result = await Lifecycle.SplitAsync(user.Id, new SplitGroupRequest(
            group.Id, "Trip", [moves.Id], null, null, null));

        var balance = await Settlements.GetGroupBalanceAsync(user.Id, result.NewGroupId);
        balance.Balances.Sum(b => b.Net).ShouldBe(0m);
        balance.Balances.Max(b => b.Net).ShouldBe(15m);
    }

    [Fact]
    public async Task Splitting_leaves_the_source_group_balanced_on_what_remains()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (group, me, bob) = await MakeGroupAsync(user.Id, "Everything", "Bob");
        await AddExpenseAsync(user.Id, group.Id, me, 20m, "Stays", me, bob);
        var moves = await AddExpenseAsync(user.Id, group.Id, me, 30m, "Moves", me, bob);

        await Lifecycle.SplitAsync(user.Id, new SplitGroupRequest(
            group.Id, "Trip", [moves.Id], null, null, null));

        var balance = await Settlements.GetGroupBalanceAsync(user.Id, group.Id);
        balance.Balances.Max(b => b.Net).ShouldBe(10m);
    }

    [Fact]
    public async Task Splitting_moves_the_relevant_log_entries_and_keeps_their_lineage()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (group, me, bob) = await MakeGroupAsync(user.Id, "Everything", "Bob");
        var moves = await AddExpenseAsync(user.Id, group.Id, me, 30m, "Moves", me, bob);
        var originalLineage = (await Db.Groups.FirstAsync(g => g.Id == group.Id)).LineageId;

        var result = await Lifecycle.SplitAsync(user.Id, new SplitGroupRequest(
            group.Id, "Trip", [moves.Id], null, null, null));

        var moved = await NewContext().SyncLog
            .Where(e => e.GroupId == result.NewGroupId && e.EntityId == moves.Id)
            .ToListAsync();
        moved.ShouldNotBeEmpty();
        moved.ShouldAllBe(e => e.LineageId == originalLineage);
    }

    [Fact]
    public async Task Splitting_writes_a_marker_entry_in_both_logs()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (group, me, bob) = await MakeGroupAsync(user.Id, "Everything", "Bob");
        var moves = await AddExpenseAsync(user.Id, group.Id, me, 30m, "Moves", me, bob);

        var result = await Lifecycle.SplitAsync(user.Id, new SplitGroupRequest(
            group.Id, "Trip", [moves.Id], null, null, null));

        var fresh = NewContext();
        (await fresh.SyncLog.AnyAsync(e =>
            e.GroupId == group.Id && e.Operation == SyncOperation.Split)).ShouldBeTrue();
        (await fresh.SyncLog.AnyAsync(e =>
            e.GroupId == result.NewGroupId && e.Operation == SyncOperation.Split)).ShouldBeTrue();
    }

    [Fact]
    public async Task The_new_group_inherits_the_clock_of_the_history_it_took()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (group, me, bob) = await MakeGroupAsync(user.Id, "Everything", "Bob");
        var moves = await AddExpenseAsync(user.Id, group.Id, me, 30m, "Moves", me, bob);
        var expenseClock = (await Db.Expenses.FirstAsync(e => e.Id == moves.Id)).Clock;

        var result = await Lifecycle.SplitAsync(user.Id, new SplitGroupRequest(
            group.Id, "Trip", [moves.Id], null, null, null));

        // Without this, a device that already knew the expense would treat every
        // moved revision as unseen and re-conflict with itself.
        (await NewContext().Groups.FirstAsync(g => g.Id == result.NewGroupId))
            .Clock.Dominates(expenseClock).ShouldBeTrue();
    }

    [Fact]
    public async Task Splitting_records_a_lineage_link()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (group, me, bob) = await MakeGroupAsync(user.Id, "Everything", "Bob");
        var moves = await AddExpenseAsync(user.Id, group.Id, me, 30m, "Moves", me, bob);

        var result = await Lifecycle.SplitAsync(user.Id, new SplitGroupRequest(
            group.Id, "Trip", [moves.Id], null, null, "Holiday apart"));

        var link = await NewContext().GroupLineageLinks.SingleAsync(l => l.Id == result.LineageLinkId);
        link.Kind.ShouldBe(GroupLineageKind.Split);
        link.SourceGroupId.ShouldBe(group.Id);
        link.TargetGroupId.ShouldBe(result.NewGroupId);
    }

    [Fact]
    public async Task Splitting_moves_the_comments_and_revisions_with_the_expense()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (group, me, bob) = await MakeGroupAsync(user.Id, "Everything", "Bob");
        var moves = await AddExpenseAsync(user.Id, group.Id, me, 30m, "Moves", me, bob);
        await Expenses.AddCommentAsync(user.Id, new CreateCommentRequest(moves.Id, "Note", null, null));

        var result = await Lifecycle.SplitAsync(user.Id, new SplitGroupRequest(
            group.Id, "Trip", [moves.Id], null, null, null));

        var fresh = NewContext();
        (await fresh.ExpenseComments.SingleAsync(c => c.ExpenseId == moves.Id))
            .GroupId.ShouldBe(result.NewGroupId);
        (await fresh.ExpenseRevisions.Where(r => r.ExpenseId == moves.Id).ToListAsync())
            .ShouldAllBe(r => r.GroupId == result.NewGroupId);
    }

    [Fact]
    public async Task Splitting_out_nothing_is_rejected()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (group, _, _) = await MakeGroupAsync(user.Id, "Everything", "Bob");

        await Should.ThrowAsync<ValidationException>(() => Lifecycle.SplitAsync(
            user.Id, new SplitGroupRequest(group.Id, "Trip", [], null, null, null)));
    }

    [Fact]
    public async Task Splitting_an_expense_from_another_group_is_rejected()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (group, _, _) = await MakeGroupAsync(user.Id, "Everything", "Bob");
        var (other, otherMe, otherBob) = await MakeGroupAsync(user.Id, "Other", "Bob");
        var elsewhere = await AddExpenseAsync(user.Id, other.Id, otherMe, 10m, "Elsewhere", otherMe, otherBob);

        await Should.ThrowAsync<ValidationException>(() => Lifecycle.SplitAsync(
            user.Id, new SplitGroupRequest(group.Id, "Trip", [elsewhere.Id], null, null, null)));
    }

    [Fact]
    public async Task The_new_group_needs_a_name()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (group, me, bob) = await MakeGroupAsync(user.Id, "Everything", "Bob");
        var moves = await AddExpenseAsync(user.Id, group.Id, me, 30m, "Moves", me, bob);

        await Should.ThrowAsync<ValidationException>(() => Lifecycle.SplitAsync(
            user.Id, new SplitGroupRequest(group.Id, "  ", [moves.Id], null, null, null)));
    }

    [Fact]
    public async Task A_merge_followed_by_a_split_returns_the_history_to_its_own_group()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (target, targetMe, targetBob) = await MakeGroupAsync(user.Id, "Keep", "Bob");
        var (source, sourceMe, sourceBob) = await MakeGroupAsync(user.Id, "Fold in", "Bob");
        var expense = await AddExpenseAsync(user.Id, source.Id, sourceMe, 60m, "Round trip", sourceMe, sourceBob);

        await Lifecycle.MergeAsync(user.Id, new MergeGroupsRequest(
            source.Id, target.Id,
            new Dictionary<Guid, Guid> { [sourceMe] = targetMe, [sourceBob] = targetBob }, null));

        var split = await Lifecycle.SplitAsync(user.Id, new SplitGroupRequest(
            target.Id, "Back out", [expense.Id], null, null, null));

        var moved = await NewContext().Expenses.FirstAsync(e => e.Id == expense.Id);
        moved.GroupId.ShouldBe(split.NewGroupId);
        var balance = await Settlements.GetGroupBalanceAsync(user.Id, split.NewGroupId);
        balance.Balances.Sum(b => b.Net).ShouldBe(0m);
    }

    // ---- transfer --------------------------------------------------------

    [Fact]
    public async Task Transferring_moves_an_expense_between_groups()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (from, fromMe, fromBob) = await MakeGroupAsync(user.Id, "Wrong group", "Bob");
        var (to, toMe, toBob) = await MakeGroupAsync(user.Id, "Right group", "Bob");
        var expense = await AddExpenseAsync(user.Id, from.Id, fromMe, 40m, "Misfiled", fromMe, fromBob);

        var result = await Lifecycle.TransferExpenseAsync(user.Id, new TransferExpenseRequest(
            expense.Id, to.Id, new Dictionary<Guid, Guid> { [fromMe] = toMe, [fromBob] = toBob }));

        result.FromGroupId.ShouldBe(from.Id);
        result.ToGroupId.ShouldBe(to.Id);
        (await NewContext().Expenses.FirstAsync(e => e.Id == expense.Id)).GroupId.ShouldBe(to.Id);
    }

    [Fact]
    public async Task A_transferred_expense_keeps_its_id_rather_than_being_recreated()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (from, fromMe, fromBob) = await MakeGroupAsync(user.Id, "Wrong group", "Bob");
        var (to, toMe, toBob) = await MakeGroupAsync(user.Id, "Right group", "Bob");
        var expense = await AddExpenseAsync(user.Id, from.Id, fromMe, 40m, "Misfiled", fromMe, fromBob);

        await Lifecycle.TransferExpenseAsync(user.Id, new TransferExpenseRequest(
            expense.Id, to.Id, new Dictionary<Guid, Guid> { [fromMe] = toMe, [fromBob] = toBob }));

        (await NewContext().Expenses.CountAsync(e => e.Id == expense.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task A_transfer_carries_the_revisions_comments_and_log_entries()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (from, fromMe, fromBob) = await MakeGroupAsync(user.Id, "Wrong group", "Bob");
        var (to, toMe, toBob) = await MakeGroupAsync(user.Id, "Right group", "Bob");
        var expense = await AddExpenseAsync(user.Id, from.Id, fromMe, 40m, "Misfiled", fromMe, fromBob);
        await Expenses.AddCommentAsync(user.Id, new CreateCommentRequest(expense.Id, "Wrong group", null, null));
        await Expenses.UpdateAsync(user.Id, expense.Id, new UpdateExpenseRequest(
            null, "Misfiled dinner", null, null, null, null, null, null, null, null, null, null));

        var result = await Lifecycle.TransferExpenseAsync(user.Id, new TransferExpenseRequest(
            expense.Id, to.Id, new Dictionary<Guid, Guid> { [fromMe] = toMe, [fromBob] = toBob }));

        result.MovedRevisions.ShouldBe(2);
        result.MovedComments.ShouldBe(1);
        result.MovedLogEntries.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_transferred_expense_remembers_where_it_came_from()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (from, fromMe, fromBob) = await MakeGroupAsync(user.Id, "Wrong group", "Bob");
        var (to, toMe, toBob) = await MakeGroupAsync(user.Id, "Right group", "Bob");
        var expense = await AddExpenseAsync(user.Id, from.Id, fromMe, 40m, "Misfiled", fromMe, fromBob);

        await Lifecycle.TransferExpenseAsync(user.Id, new TransferExpenseRequest(
            expense.Id, to.Id, new Dictionary<Guid, Guid> { [fromMe] = toMe, [fromBob] = toBob }));

        var moved = await NewContext().Expenses.FirstAsync(e => e.Id == expense.Id);
        moved.OriginGroupId.ShouldBe(from.Id);
    }

    [Fact]
    public async Task A_transfer_writes_a_transfer_entry_naming_the_source_group()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (from, fromMe, fromBob) = await MakeGroupAsync(user.Id, "Wrong group", "Bob");
        var (to, toMe, toBob) = await MakeGroupAsync(user.Id, "Right group", "Bob");
        var expense = await AddExpenseAsync(user.Id, from.Id, fromMe, 40m, "Misfiled", fromMe, fromBob);

        await Lifecycle.TransferExpenseAsync(user.Id, new TransferExpenseRequest(
            expense.Id, to.Id, new Dictionary<Guid, Guid> { [fromMe] = toMe, [fromBob] = toBob }));

        var entry = await NewContext().SyncLog
            .FirstAsync(e => e.GroupId == to.Id && e.Operation == SyncOperation.Transfer);
        entry.SourceGroupId.ShouldBe(from.Id);
        entry.EntityId.ShouldBe(expense.Id);
    }

    [Fact]
    public async Task Transferring_moves_the_balance_from_one_group_to_the_other()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (from, fromMe, fromBob) = await MakeGroupAsync(user.Id, "Wrong group", "Bob");
        var (to, toMe, toBob) = await MakeGroupAsync(user.Id, "Right group", "Bob");
        var expense = await AddExpenseAsync(user.Id, from.Id, fromMe, 40m, "Misfiled", fromMe, fromBob);

        await Lifecycle.TransferExpenseAsync(user.Id, new TransferExpenseRequest(
            expense.Id, to.Id, new Dictionary<Guid, Guid> { [fromMe] = toMe, [fromBob] = toBob }));

        (await Settlements.GetGroupBalanceAsync(user.Id, from.Id))
            .Balances.ShouldAllBe(b => b.Net == 0m);
        (await Settlements.GetGroupBalanceAsync(user.Id, to.Id))
            .Balances.First(b => b.MemberId == toMe).Net.ShouldBe(20m);
    }

    [Fact]
    public async Task Transferring_into_a_group_you_are_not_in_is_forbidden()
    {
        var owner = await TestData.SeedUserAsync(Db, "Alice");
        var stranger = await TestData.SeedUserAsync(Db, "Stranger");
        var (from, fromMe, fromBob) = await MakeGroupAsync(owner.Id, "Wrong group", "Bob");
        var (to, _, _) = await MakeGroupAsync(stranger.Id, "Not mine", "Bob");
        var expense = await AddExpenseAsync(owner.Id, from.Id, fromMe, 40m, "Misfiled", fromMe, fromBob);

        await Should.ThrowAsync<ForbiddenException>(() => Lifecycle.TransferExpenseAsync(
            owner.Id, new TransferExpenseRequest(expense.Id, to.Id, null)));
    }

    [Fact]
    public async Task Transferring_into_the_same_group_is_rejected()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (group, me, bob) = await MakeGroupAsync(user.Id, "Only", "Bob");
        var expense = await AddExpenseAsync(user.Id, group.Id, me, 40m, "Here", me, bob);

        await Should.ThrowAsync<ValidationException>(() => Lifecycle.TransferExpenseAsync(
            user.Id, new TransferExpenseRequest(expense.Id, group.Id, null)));
    }

    [Fact]
    public async Task A_transfer_with_an_unmappable_member_is_rejected()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (from, fromMe, fromZoe) = await MakeGroupAsync(user.Id, "Wrong group", "Zoe");
        var to = await Groups.CreateAsync(user.Id, new CreateGroupRequest("Right group", "CAD", null, null, null, null));
        var expense = await AddExpenseAsync(user.Id, from.Id, fromZoe, 40m, "Zoe paid", fromMe, fromZoe);

        // Guessing would silently reassign a debt to the wrong person.
        await Should.ThrowAsync<ValidationException>(() => Lifecycle.TransferExpenseAsync(
            user.Id, new TransferExpenseRequest(expense.Id, to.Id, null)));
    }

    // ---- compaction ------------------------------------------------------

    [Fact]
    public async Task Compaction_collapses_settled_history_into_a_snapshot()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (group, me, bob) = await MakeGroupAsync(user.Id, "Long lived", "Bob");
        await AddExpenseAsync(user.Id, group.Id, me, 40m, "Old", me, bob);
        Clock.Advance(TimeSpan.FromDays(400));

        var result = await Lifecycle.CompactAsync(group.Id, Clock.UtcNow.AddYears(-1));

        result.SnapshotId.ShouldNotBeNull();
        result.CompactedEntries.ShouldBeGreaterThan(0);
        (await NewContext().SyncSnapshots.CountAsync(s => s.GroupId == group.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task Compaction_trims_the_entries_it_replaced()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (group, me, bob) = await MakeGroupAsync(user.Id, "Long lived", "Bob");
        await AddExpenseAsync(user.Id, group.Id, me, 40m, "Old", me, bob);
        Clock.Advance(TimeSpan.FromDays(400));

        var result = await Lifecycle.CompactAsync(group.Id, Clock.UtcNow.AddYears(-1));

        result.TrimmedEntries.ShouldBe(result.CompactedEntries);
        (await NewContext().SyncLog.CountAsync(e =>
            e.GroupId == group.Id && e.ServerSeq <= result.UpToServerSeq)).ShouldBe(0);
    }

    [Fact]
    public async Task The_snapshot_holds_the_joined_clock_of_what_it_replaced()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (group, me, bob) = await MakeGroupAsync(user.Id, "Long lived", "Bob");
        var expense = await AddExpenseAsync(user.Id, group.Id, me, 40m, "Old", me, bob);
        var expenseClock = (await Db.Expenses.FirstAsync(e => e.Id == expense.Id)).Clock;
        Clock.Advance(TimeSpan.FromDays(400));

        await Lifecycle.CompactAsync(group.Id, Clock.UtcNow.AddYears(-1));

        var snapshot = await NewContext().SyncSnapshots.SingleAsync(s => s.GroupId == group.Id);
        SplitEverything.Domain.Sync.VectorClock.FromJson(snapshot.VectorClockJson)
            .Dominates(expenseClock).ShouldBeTrue();
    }

    [Fact]
    public async Task The_snapshot_still_carries_the_surviving_expenses()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (group, me, bob) = await MakeGroupAsync(user.Id, "Long lived", "Bob");
        var expense = await AddExpenseAsync(user.Id, group.Id, me, 40m, "Old", me, bob);
        Clock.Advance(TimeSpan.FromDays(400));

        await Lifecycle.CompactAsync(group.Id, Clock.UtcNow.AddYears(-1));

        var snapshot = await NewContext().SyncSnapshots.SingleAsync(s => s.GroupId == group.Id);
        snapshot.StateJson.ShouldContain(expense.Id.ToString());
    }

    [Fact]
    public async Task Compaction_leaves_recent_history_in_the_live_log()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (group, me, bob) = await MakeGroupAsync(user.Id, "Long lived", "Bob");
        await AddExpenseAsync(user.Id, group.Id, me, 40m, "Old", me, bob);
        Clock.Advance(TimeSpan.FromDays(400));
        var recent = await AddExpenseAsync(user.Id, group.Id, me, 10m, "Recent", me, bob);

        await Lifecycle.CompactAsync(group.Id, Clock.UtcNow.AddYears(-1));

        (await NewContext().SyncLog.AnyAsync(e =>
            e.GroupId == group.Id && e.EntityId == recent.Id)).ShouldBeTrue();
    }

    [Fact]
    public async Task Compaction_with_nothing_old_enough_does_nothing()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (group, me, bob) = await MakeGroupAsync(user.Id, "Fresh", "Bob");
        await AddExpenseAsync(user.Id, group.Id, me, 40m, "New", me, bob);

        var result = await Lifecycle.CompactAsync(group.Id, Clock.UtcNow.AddYears(-1));

        result.SnapshotId.ShouldBeNull();
        result.CompactedEntries.ShouldBe(0);
    }

    [Fact]
    public async Task A_device_behind_the_cutoff_is_handed_the_snapshot_instead_of_trimmed_entries()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (group, me, bob) = await MakeGroupAsync(user.Id, "Long lived", "Bob");
        await AddExpenseAsync(user.Id, group.Id, me, 40m, "Old", me, bob);
        Clock.Advance(TimeSpan.FromDays(400));
        await Lifecycle.CompactAsync(group.Id, Clock.UtcNow.AddYears(-1));

        var sync = new SyncService(Db, Writer, Broadcaster, Clock);
        var result = await sync.PullAsync(user.Id, new SplitEverything.Application.Contracts.Sync.SyncPullRequest(
            TestData.DeviceB, new Dictionary<Guid, long> { [group.Id] = 0 }));

        result.Snapshots.ShouldHaveSingleItem().GroupId.ShouldBe(group.Id);
    }

    [Fact]
    public async Task Compaction_does_not_lose_the_balance_of_the_group()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (group, me, bob) = await MakeGroupAsync(user.Id, "Long lived", "Bob");
        await AddExpenseAsync(user.Id, group.Id, me, 40m, "Old", me, bob);
        var before = await Settlements.GetGroupBalanceAsync(user.Id, group.Id);
        Clock.Advance(TimeSpan.FromDays(400));

        await Lifecycle.CompactAsync(group.Id, Clock.UtcNow.AddYears(-1));

        var after = await Settlements.GetGroupBalanceAsync(user.Id, group.Id);
        after.Balances.Select(b => b.Net).ShouldBe(before.Balances.Select(b => b.Net));
    }

    [Fact]
    public async Task Compacting_an_unknown_group_is_a_not_found()
        => await Should.ThrowAsync<NotFoundException>(
            () => Lifecycle.CompactAsync(Guid.NewGuid(), Clock.UtcNow));
}
