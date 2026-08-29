using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Expenses;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Application.Contracts.Sync;
using SplitEverything.Domain.Common;
using SplitEverything.Domain.Sync;
using SplitEverything.Infrastructure.Services;
using SplitEverything.Infrastructure.Sync;
using SplitEverything.Tests.Support;

namespace SplitEverything.Tests.Application;

/// <summary>
/// The offline story end to end: a device queues changes, comes back, and the
/// server has to apply, ignore or flag each one without ever losing an edit.
/// </summary>
public class SyncServiceTests(PostgresFixture fixture) : ServiceTestBase(fixture)
{
    private SyncService Sync => new(Db, Writer, Broadcaster, Clock);

    private async Task<(Guid UserId, GroupDto Group, Guid Alice, Guid Bob)> SetupAsync()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, ["Bob"]));
        return (user.Id, group,
            group.Members.First(m => m.UserId == user.Id).Id,
            group.Members.First(m => m.DisplayName == "Bob").Id);
    }

    private static string ExpenseJson(
        Guid id, Guid groupId, Guid payer, decimal amount, string description,
        IReadOnlyList<(Guid MemberId, decimal Amount)> splits)
        => JsonSerializer.Serialize(new
        {
            id,
            groupId,
            paidByMemberId = payer,
            description,
            amount,
            currency = "CAD",
            amountInBaseCurrency = amount,
            exchangeRate = 1m,
            spentAt = TestData.Jan1,
            splitType = (int)SplitType.Equal,
            splits = splits.Select(s => new { memberId = s.MemberId, amount = s.Amount, amountInBaseCurrency = s.Amount })
        });

    private static Dictionary<string, long> Clocks(params (string Device, long Value)[] entries)
        => entries.ToDictionary(e => e.Device, e => e.Value);

    [Fact]
    public async Task An_offline_create_is_applied_on_reconnect()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        var expenseId = Guid.CreateVersion7();

        var result = await Sync.PushAsync(userId, new SyncPushRequest(TestData.DeviceB, [
            Operation(group.Id, SyncEntityType.Expense, expenseId, SyncOperation.Create,
                ExpenseJson(expenseId, group.Id, alice, 40m, "Offline dinner", [(alice, 20m), (bob, 20m)]),
                Clocks((TestData.DeviceB, 1)))
        ]));

        result.Accepted.ShouldHaveSingleItem().EntityId.ShouldBe(expenseId);
        result.Conflicts.ShouldBeEmpty();

        var stored = await NewContext().Expenses.FirstOrDefaultAsync(e => e.Id == expenseId);
        stored.ShouldNotBeNull();
        stored.Description.ShouldBe("Offline dinner");
        stored.Amount.ShouldBe(40m);
    }

    [Fact]
    public async Task An_offline_create_brings_its_splits_with_it()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        var expenseId = Guid.CreateVersion7();

        await Sync.PushAsync(userId, new SyncPushRequest(TestData.DeviceB, [
            Operation(group.Id, SyncEntityType.Expense, expenseId, SyncOperation.Create,
                ExpenseJson(expenseId, group.Id, alice, 40m, "Dinner", [(alice, 20m), (bob, 20m)]),
                Clocks((TestData.DeviceB, 1)))
        ]));

        var splits = await NewContext().ExpenseSplits.Where(s => s.ExpenseId == expenseId).ToListAsync();
        splits.Count.ShouldBe(2);
        splits.Sum(s => s.Amount).ShouldBe(40m);
    }

    [Fact]
    public async Task Pushing_the_same_operation_twice_is_a_no_op()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        var expenseId = Guid.CreateVersion7();
        var operation = Operation(group.Id, SyncEntityType.Expense, expenseId, SyncOperation.Create,
            ExpenseJson(expenseId, group.Id, alice, 40m, "Dinner", [(alice, 20m), (bob, 20m)]),
            Clocks((TestData.DeviceB, 1)));

        await Sync.PushAsync(userId, new SyncPushRequest(TestData.DeviceB, [operation]));
        var second = await Sync.PushAsync(userId, new SyncPushRequest(TestData.DeviceB, [operation]));

        second.Conflicts.ShouldBeEmpty();
        second.Rejected.ShouldBeEmpty();
        (await NewContext().Expenses.CountAsync(e => e.Id == expenseId)).ShouldBe(1);
    }

    [Fact]
    public async Task A_newer_offline_edit_wins_over_what_is_stored()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        var expense = await Expenses.CreateAsync(userId, new CreateExpenseRequest(
            group.Id, alice, "Original", 40m, "CAD", TestData.Jan1, SplitType.Equal,
            [new SplitInputDto(alice, null), new SplitInputDto(bob, null)],
            null, null, null, null, null, null, null));

        // The device saw the stored revision and edited on top of it.
        var newer = new Dictionary<string, long>(expense.VectorClock) { [TestData.DeviceB] = 1 };

        var result = await Sync.PushAsync(userId, new SyncPushRequest(TestData.DeviceB, [
            Operation(group.Id, SyncEntityType.Expense, expense.Id, SyncOperation.Update,
                ExpenseJson(expense.Id, group.Id, alice, 55m, "Edited offline", [(alice, 27.50m), (bob, 27.50m)]),
                newer)
        ]));

        result.Accepted.ShouldHaveSingleItem();
        var stored = await NewContext().Expenses.FirstAsync(e => e.Id == expense.Id);
        stored.Description.ShouldBe("Edited offline");
        stored.Amount.ShouldBe(55m);
    }

    [Fact]
    public async Task A_stale_offline_edit_is_dropped_without_touching_stored_state()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        var expense = await Expenses.CreateAsync(userId, new CreateExpenseRequest(
            group.Id, alice, "Current", 40m, "CAD", TestData.Jan1, SplitType.Equal,
            [new SplitInputDto(alice, null)], null, null, null, null, null, null, null));

        // A clock strictly behind what is stored: some other device already carried
        // the group past this revision.
        var stale = expense.VectorClock.ToDictionary(kv => kv.Key, kv => kv.Value);
        var device = stale.Keys.First();
        stale[device] = Math.Max(0, stale[device] - 1);

        var result = await Sync.PushAsync(userId, new SyncPushRequest(TestData.DeviceB, [
            Operation(group.Id, SyncEntityType.Expense, expense.Id, SyncOperation.Update,
                ExpenseJson(expense.Id, group.Id, alice, 999m, "Stale", [(alice, 999m)]),
                stale)
        ]));

        result.Conflicts.ShouldBeEmpty();
        (await NewContext().Expenses.FirstAsync(e => e.Id == expense.Id)).Description.ShouldBe("Current");
    }

    [Fact]
    public async Task Two_devices_editing_the_same_expense_offline_produce_a_conflict()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        var expense = await Expenses.CreateAsync(userId, new CreateExpenseRequest(
            group.Id, alice, "Original", 40m, "CAD", TestData.Jan1, SplitType.Equal,
            [new SplitInputDto(alice, null)], null, null, null, null, null, null, null));

        // Both devices branched from the stored revision, neither saw the other.
        var fromA = new Dictionary<string, long>(expense.VectorClock) { [TestData.DeviceA] = 9 };
        var fromB = new Dictionary<string, long>(expense.VectorClock) { [TestData.DeviceB] = 9 };

        await Sync.PushAsync(userId, new SyncPushRequest(TestData.DeviceA, [
            Operation(group.Id, SyncEntityType.Expense, expense.Id, SyncOperation.Update,
                ExpenseJson(expense.Id, group.Id, alice, 50m, "Edit from A", [(alice, 50m)]), fromA)
        ]));

        var result = await Sync.PushAsync(userId, new SyncPushRequest(TestData.DeviceB, [
            Operation(group.Id, SyncEntityType.Expense, expense.Id, SyncOperation.Update,
                ExpenseJson(expense.Id, group.Id, alice, 60m, "Edit from B", [(alice, 60m)]), fromB)
        ]));

        var conflict = result.Conflicts.ShouldHaveSingleItem();
        conflict.EntityId.ShouldBe(expense.Id);
        conflict.ConflictingFields.ShouldContain("description");
    }

    [Fact]
    public async Task A_conflicting_edit_never_overwrites_the_stored_revision()
    {
        var (userId, group, alice, _) = await SetupAsync();
        var expense = await Expenses.CreateAsync(userId, new CreateExpenseRequest(
            group.Id, alice, "Original", 40m, "CAD", TestData.Jan1, SplitType.Equal,
            [new SplitInputDto(alice, null)], null, null, null, null, null, null, null));

        var fromA = new Dictionary<string, long>(expense.VectorClock) { [TestData.DeviceA] = 9 };
        var fromB = new Dictionary<string, long>(expense.VectorClock) { [TestData.DeviceB] = 9 };
        await Sync.PushAsync(userId, new SyncPushRequest(TestData.DeviceA, [
            Operation(group.Id, SyncEntityType.Expense, expense.Id, SyncOperation.Update,
                ExpenseJson(expense.Id, group.Id, alice, 50m, "Edit from A", [(alice, 50m)]), fromA)
        ]));
        await Sync.PushAsync(userId, new SyncPushRequest(TestData.DeviceB, [
            Operation(group.Id, SyncEntityType.Expense, expense.Id, SyncOperation.Update,
                ExpenseJson(expense.Id, group.Id, alice, 60m, "Edit from B", [(alice, 60m)]), fromB)
        ]));

        (await NewContext().Expenses.FirstAsync(e => e.Id == expense.Id)).Description.ShouldBe("Edit from A");
    }

    [Fact]
    public async Task A_conflict_keeps_the_losing_payload_verbatim_for_review()
    {
        var (userId, group, alice, _) = await SetupAsync();
        var expense = await Expenses.CreateAsync(userId, new CreateExpenseRequest(
            group.Id, alice, "Original", 40m, "CAD", TestData.Jan1, SplitType.Equal,
            [new SplitInputDto(alice, null)], null, null, null, null, null, null, null));

        var fromA = new Dictionary<string, long>(expense.VectorClock) { [TestData.DeviceA] = 9 };
        var fromB = new Dictionary<string, long>(expense.VectorClock) { [TestData.DeviceB] = 9 };
        await Sync.PushAsync(userId, new SyncPushRequest(TestData.DeviceA, [
            Operation(group.Id, SyncEntityType.Expense, expense.Id, SyncOperation.Update,
                ExpenseJson(expense.Id, group.Id, alice, 50m, "Edit from A", [(alice, 50m)]), fromA)
        ]));
        await Sync.PushAsync(userId, new SyncPushRequest(TestData.DeviceB, [
            Operation(group.Id, SyncEntityType.Expense, expense.Id, SyncOperation.Update,
                ExpenseJson(expense.Id, group.Id, alice, 60m, "Edit from B", [(alice, 60m)]), fromB)
        ]));

        var stored = await NewContext().SyncConflicts.SingleAsync();
        stored.IncomingPayloadJson.ShouldContain("Edit from B");
        stored.StoredPayloadJson.ShouldContain("Edit from A");
        stored.Resolution.ShouldBe(ConflictResolution.Unresolved);
    }

    [Fact]
    public async Task Open_conflicts_are_listed_for_the_user_to_resolve()
    {
        var (userId, group, alice, _) = await SetupAsync();
        await CreateConflictAsync(userId, group.Id, alice);

        var conflicts = await Sync.GetOpenConflictsAsync(userId);

        conflicts.ShouldHaveSingleItem().GroupId.ShouldBe(group.Id);
    }

    [Fact]
    public async Task Keeping_the_remote_side_applies_the_payload_that_lost()
    {
        var (userId, group, alice, _) = await SetupAsync();
        var expenseId = await CreateConflictAsync(userId, group.Id, alice);
        var conflict = (await Sync.GetOpenConflictsAsync(userId)).Single();

        await Sync.ResolveConflictAsync(userId,
            new ResolveConflictRequest(conflict.ConflictId, ConflictResolution.KeepRemote, null));

        (await NewContext().Expenses.FirstAsync(e => e.Id == expenseId)).Description.ShouldBe("Edit from B");
    }

    [Fact]
    public async Task Keeping_the_local_side_leaves_stored_state_alone()
    {
        var (userId, group, alice, _) = await SetupAsync();
        var expenseId = await CreateConflictAsync(userId, group.Id, alice);
        var conflict = (await Sync.GetOpenConflictsAsync(userId)).Single();

        await Sync.ResolveConflictAsync(userId,
            new ResolveConflictRequest(conflict.ConflictId, ConflictResolution.KeepLocal, null));

        (await NewContext().Expenses.FirstAsync(e => e.Id == expenseId)).Description.ShouldBe("Edit from A");
    }

    [Fact]
    public async Task A_resolved_conflict_drops_off_the_open_list()
    {
        var (userId, group, alice, _) = await SetupAsync();
        await CreateConflictAsync(userId, group.Id, alice);
        var conflict = (await Sync.GetOpenConflictsAsync(userId)).Single();

        await Sync.ResolveConflictAsync(userId,
            new ResolveConflictRequest(conflict.ConflictId, ConflictResolution.KeepLocal, null));

        (await Sync.GetOpenConflictsAsync(userId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Resolving_with_a_merged_payload_writes_the_merge()
    {
        var (userId, group, alice, _) = await SetupAsync();
        var expenseId = await CreateConflictAsync(userId, group.Id, alice);
        var conflict = (await Sync.GetOpenConflictsAsync(userId)).Single();
        var merged = ExpenseJson(expenseId, group.Id, alice, 55m, "Merged by hand", [(alice, 55m)]);

        await Sync.ResolveConflictAsync(userId,
            new ResolveConflictRequest(conflict.ConflictId, ConflictResolution.Merged, merged));

        (await NewContext().Expenses.FirstAsync(e => e.Id == expenseId)).Description.ShouldBe("Merged by hand");
    }

    [Fact]
    public async Task A_merge_resolution_needs_a_payload()
    {
        var (userId, group, alice, _) = await SetupAsync();
        await CreateConflictAsync(userId, group.Id, alice);
        var conflict = (await Sync.GetOpenConflictsAsync(userId)).Single();

        await Should.ThrowAsync<ValidationException>(() => Sync.ResolveConflictAsync(
            userId, new ResolveConflictRequest(conflict.ConflictId, ConflictResolution.Merged, null)));
    }

    [Fact]
    public async Task Resolving_someone_elses_conflict_is_forbidden()
    {
        var (userId, group, alice, _) = await SetupAsync();
        await CreateConflictAsync(userId, group.Id, alice);
        var conflict = (await Sync.GetOpenConflictsAsync(userId)).Single();
        var stranger = await TestData.SeedUserAsync(Db, "Stranger");

        await Should.ThrowAsync<ForbiddenException>(() => Sync.ResolveConflictAsync(
            stranger.Id, new ResolveConflictRequest(conflict.ConflictId, ConflictResolution.KeepLocal, null)));
    }

    [Fact]
    public async Task Pushing_into_a_group_you_are_not_in_is_rejected_not_thrown()
    {
        var (_, group, alice, bob) = await SetupAsync();
        var stranger = await TestData.SeedUserAsync(Db, "Stranger");
        var expenseId = Guid.CreateVersion7();

        var result = await Sync.PushAsync(stranger.Id, new SyncPushRequest(TestData.DeviceB, [
            Operation(group.Id, SyncEntityType.Expense, expenseId, SyncOperation.Create,
                ExpenseJson(expenseId, group.Id, alice, 40m, "Not mine", [(alice, 40m)]),
                Clocks((TestData.DeviceB, 1)))
        ]));

        // One bad operation must not fail the whole batch: the rest of the queue
        // still needs to drain.
        result.Rejected.ShouldHaveSingleItem().Code.ShouldBe("Forbidden");
        result.Accepted.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_batch_applies_the_good_operations_and_reports_the_bad_one()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        var goodId = Guid.CreateVersion7();
        var orphanGroupId = Guid.NewGuid();
        var badId = Guid.CreateVersion7();

        var result = await Sync.PushAsync(userId, new SyncPushRequest(TestData.DeviceB, [
            Operation(group.Id, SyncEntityType.Expense, goodId, SyncOperation.Create,
                ExpenseJson(goodId, group.Id, alice, 10m, "Good", [(alice, 10m)]),
                Clocks((TestData.DeviceB, 1))),
            Operation(orphanGroupId, SyncEntityType.Expense, badId, SyncOperation.Create,
                ExpenseJson(badId, orphanGroupId, alice, 10m, "Bad", [(alice, 10m)]),
                Clocks((TestData.DeviceB, 2)))
        ]));

        result.Accepted.ShouldHaveSingleItem().EntityId.ShouldBe(goodId);
        result.Rejected.ShouldHaveSingleItem().EntityId.ShouldBe(badId);
    }

    [Fact]
    public async Task An_unparseable_payload_is_rejected()
    {
        var (userId, group, _, _) = await SetupAsync();

        var result = await Sync.PushAsync(userId, new SyncPushRequest(TestData.DeviceB, [
            Operation(group.Id, SyncEntityType.Expense, Guid.CreateVersion7(), SyncOperation.Create,
                "this is not json", Clocks((TestData.DeviceB, 1)))
        ]));

        result.Rejected.ShouldHaveSingleItem().Code.ShouldBe("InvalidPayload");
    }

    [Fact]
    public async Task An_offline_delete_tombstones_the_expense()
    {
        var (userId, group, alice, _) = await SetupAsync();
        var expense = await Expenses.CreateAsync(userId, new CreateExpenseRequest(
            group.Id, alice, "Doomed", 40m, "CAD", TestData.Jan1, SplitType.Equal,
            [new SplitInputDto(alice, null)], null, null, null, null, null, null, null));
        var newer = new Dictionary<string, long>(expense.VectorClock) { [TestData.DeviceB] = 1 };

        await Sync.PushAsync(userId, new SyncPushRequest(TestData.DeviceB, [
            Operation(group.Id, SyncEntityType.Expense, expense.Id, SyncOperation.Delete, "{}", newer)
        ]));

        (await NewContext().Expenses.FirstAsync(e => e.Id == expense.Id)).IsDeleted.ShouldBeTrue();
    }

    [Fact]
    public async Task An_offline_settlement_is_applied()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        var settlementId = Guid.CreateVersion7();
        var payload = JsonSerializer.Serialize(new
        {
            id = settlementId, groupId = group.Id,
            fromMemberId = bob, toMemberId = alice,
            amount = 25m, currency = "CAD", amountInBaseCurrency = 25m,
            settledAt = TestData.Jan1
        });

        var result = await Sync.PushAsync(userId, new SyncPushRequest(TestData.DeviceB, [
            Operation(group.Id, SyncEntityType.Settlement, settlementId, SyncOperation.Create,
                payload, Clocks((TestData.DeviceB, 1)))
        ]));

        result.Accepted.ShouldHaveSingleItem();
        (await NewContext().Settlements.FirstAsync(s => s.Id == settlementId)).Amount.ShouldBe(25m);
    }

    [Fact]
    public async Task An_offline_comment_is_applied()
    {
        var (userId, group, alice, _) = await SetupAsync();
        var expense = await Expenses.CreateAsync(userId, new CreateExpenseRequest(
            group.Id, alice, "Dinner", 40m, "CAD", TestData.Jan1, SplitType.Equal,
            [new SplitInputDto(alice, null)], null, null, null, null, null, null, null));
        var commentId = Guid.CreateVersion7();
        var payload = JsonSerializer.Serialize(new
        {
            id = commentId, expenseId = expense.Id, groupId = group.Id,
            authorMemberId = alice, body = "Added while offline"
        });

        await Sync.PushAsync(userId, new SyncPushRequest(TestData.DeviceB, [
            Operation(group.Id, SyncEntityType.ExpenseComment, commentId, SyncOperation.Create,
                payload, Clocks((TestData.DeviceB, 1)))
        ]));

        (await NewContext().ExpenseComments.FirstAsync(c => c.Id == commentId)).Body.ShouldBe("Added while offline");
    }

    [Fact]
    public async Task An_accepted_push_is_broadcast_to_the_other_live_clients()
    {
        var (userId, group, alice, _) = await SetupAsync();
        var expenseId = Guid.CreateVersion7();

        await Sync.PushAsync(userId, new SyncPushRequest(TestData.DeviceB, [
            Operation(group.Id, SyncEntityType.Expense, expenseId, SyncOperation.Create,
                ExpenseJson(expenseId, group.Id, alice, 10m, "Live", [(alice, 10m)]),
                Clocks((TestData.DeviceB, 1)))
        ]));

        await Broadcaster.Received().BroadcastAsync(
            group.Id, Arg.Any<SyncPushResult>(), TestData.DeviceB, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pulling_from_zero_returns_the_whole_group_history()
    {
        var (userId, group, alice, _) = await SetupAsync();
        await Expenses.CreateAsync(userId, new CreateExpenseRequest(
            group.Id, alice, "Dinner", 40m, "CAD", TestData.Jan1, SplitType.Equal,
            [new SplitInputDto(alice, null)], null, null, null, null, null, null, null));

        var result = await Sync.PullAsync(userId, new SyncPullRequest(
            TestData.DeviceB, new Dictionary<Guid, long> { [group.Id] = 0 }));

        result.Entries.ShouldNotBeEmpty();
        result.Entries.ShouldContain(e => e.Operation == SyncOperation.Create && e.EntityType == SyncEntityType.Expense);
    }

    [Fact]
    public async Task Pulling_from_a_cursor_returns_only_what_is_new()
    {
        var (userId, group, alice, _) = await SetupAsync();
        var first = await Sync.PullAsync(userId, new SyncPullRequest(
            TestData.DeviceB, new Dictionary<Guid, long> { [group.Id] = 0 }));
        var cursor = first.GroupCursors[group.Id];

        await Expenses.CreateAsync(userId, new CreateExpenseRequest(
            group.Id, alice, "New expense", 40m, "CAD", TestData.Jan1, SplitType.Equal,
            [new SplitInputDto(alice, null)], null, null, null, null, null, null, null));

        var second = await Sync.PullAsync(userId, new SyncPullRequest(
            TestData.DeviceB, new Dictionary<Guid, long> { [group.Id] = cursor }));

        second.Entries.ShouldAllBe(e => e.ServerSeq > cursor);
        second.Entries.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Pulling_when_nothing_changed_returns_nothing()
    {
        var (userId, group, _, _) = await SetupAsync();
        var first = await Sync.PullAsync(userId, new SyncPullRequest(
            TestData.DeviceB, new Dictionary<Guid, long> { [group.Id] = 0 }));

        var second = await Sync.PullAsync(userId, new SyncPullRequest(
            TestData.DeviceB, first.GroupCursors.ToDictionary(kv => kv.Key, kv => kv.Value)));

        second.Entries.ShouldBeEmpty();
        second.HasMore.ShouldBeFalse();
    }

    [Fact]
    public async Task Pulling_with_no_cursors_covers_every_group_the_device_follows()
    {
        var (userId, first, _, _) = await SetupAsync();
        var second = await Groups.CreateAsync(userId,
            new CreateGroupRequest("Trip", "CAD", null, null, null, null));

        var result = await Sync.PullAsync(userId, new SyncPullRequest(
            TestData.DeviceB, new Dictionary<Guid, long>()));

        result.GroupCursors.Keys.ShouldContain(first.Id);
        result.GroupCursors.Keys.ShouldContain(second.Id);
    }

    [Fact]
    public async Task Pulling_never_returns_another_users_group()
    {
        var (_, group, _, _) = await SetupAsync();
        var stranger = await TestData.SeedUserAsync(Db, "Stranger");

        var result = await Sync.PullAsync(stranger.Id, new SyncPullRequest(
            TestData.DeviceB, new Dictionary<Guid, long> { [group.Id] = 0 }));

        result.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_pull_caps_its_batch_size_and_says_there_is_more()
    {
        var (userId, group, alice, _) = await SetupAsync();
        for (var i = 0; i < 6; i++)
        {
            await Expenses.CreateAsync(userId, new CreateExpenseRequest(
                group.Id, alice, $"Expense {i}", 10m, "CAD", TestData.Jan1, SplitType.Equal,
                [new SplitInputDto(alice, null)], null, null, null, null, null, null, null));
        }

        var result = await Sync.PullAsync(userId, new SyncPullRequest(
            TestData.DeviceB, new Dictionary<Guid, long> { [group.Id] = 0 }, MaxEntries: 3));

        result.Entries.Count.ShouldBe(3);
        result.HasMore.ShouldBeTrue();
    }

    [Fact]
    public async Task The_returned_cursor_matches_the_last_entry_handed_out()
    {
        var (userId, group, alice, _) = await SetupAsync();
        await Expenses.CreateAsync(userId, new CreateExpenseRequest(
            group.Id, alice, "Dinner", 40m, "CAD", TestData.Jan1, SplitType.Equal,
            [new SplitInputDto(alice, null)], null, null, null, null, null, null, null));

        var result = await Sync.PullAsync(userId, new SyncPullRequest(
            TestData.DeviceB, new Dictionary<Guid, long> { [group.Id] = 0 }, MaxEntries: 2));

        result.GroupCursors[group.Id].ShouldBe(result.Entries.Max(e => e.ServerSeq));
    }

    [Fact]
    public async Task Acknowledging_a_cursor_registers_the_device()
    {
        var (userId, group, _, _) = await SetupAsync();

        await Sync.AcknowledgeAsync(userId, TestData.DeviceB,
            new Dictionary<Guid, long> { [group.Id] = 3 });

        var device = await NewContext().Devices.FirstOrDefaultAsync(d => d.Id == TestData.DeviceB);
        device.ShouldNotBeNull();
        device.UserId.ShouldBe(userId);
        device.LastAckedServerSeq.ShouldBe(3);
    }

    [Fact]
    public async Task Acknowledging_never_moves_a_cursor_backwards()
    {
        var (userId, group, _, _) = await SetupAsync();
        await Sync.AcknowledgeAsync(userId, TestData.DeviceB, new Dictionary<Guid, long> { [group.Id] = 10 });

        await Sync.AcknowledgeAsync(userId, TestData.DeviceB, new Dictionary<Guid, long> { [group.Id] = 4 });

        (await NewContext().Devices.FirstAsync(d => d.Id == TestData.DeviceB))
            .LastAckedServerSeq.ShouldBe(10);
    }

    [Fact]
    public async Task A_push_without_a_device_id_is_refused()
    {
        var (userId, group, _, _) = await SetupAsync();

        await Should.ThrowAsync<ValidationException>(() => Sync.PushAsync(
            userId, new SyncPushRequest("  ", [])));
    }

    [Fact]
    public async Task An_empty_push_still_returns_the_current_cursors()
    {
        var (userId, group, _, _) = await SetupAsync();

        var result = await Sync.PushAsync(userId, new SyncPushRequest(TestData.DeviceB, []));

        result.Accepted.ShouldBeEmpty();
        result.GroupCursors.ShouldNotBeEmpty();
    }

    /// <summary>Drives two divergent offline edits and returns the expense id.</summary>
    private async Task<Guid> CreateConflictAsync(Guid userId, Guid groupId, Guid alice)
    {
        var expense = await Expenses.CreateAsync(userId, new CreateExpenseRequest(
            groupId, alice, "Original", 40m, "CAD", TestData.Jan1, SplitType.Equal,
            [new SplitInputDto(alice, null)], null, null, null, null, null, null, null));

        var fromA = new Dictionary<string, long>(expense.VectorClock) { [TestData.DeviceA] = 9 };
        var fromB = new Dictionary<string, long>(expense.VectorClock) { [TestData.DeviceB] = 9 };

        await Sync.PushAsync(userId, new SyncPushRequest(TestData.DeviceA, [
            Operation(groupId, SyncEntityType.Expense, expense.Id, SyncOperation.Update,
                ExpenseJson(expense.Id, groupId, alice, 50m, "Edit from A", [(alice, 50m)]), fromA)
        ]));
        await Sync.PushAsync(userId, new SyncPushRequest(TestData.DeviceB, [
            Operation(groupId, SyncEntityType.Expense, expense.Id, SyncOperation.Update,
                ExpenseJson(expense.Id, groupId, alice, 60m, "Edit from B", [(alice, 60m)]), fromB)
        ]));

        return expense.Id;
    }
}
