using Microsoft.EntityFrameworkCore;
using Shouldly;
using SplitEverything.Application.Abstractions;
using SplitEverything.Domain.Common;
using SplitEverything.Domain.Entities;
using SplitEverything.Infrastructure.Sync;
using SplitEverything.Tests.Support;

namespace SplitEverything.Tests.Infrastructure;

/// <summary>
/// Every write in the app goes through this: it ticks the clock, takes a sequence
/// number and appends the log entry that peers will replay. Anything it forgets to
/// record is a change an offline device never learns about.
/// </summary>
public class SyncWriterTests(PostgresFixture fixture) : DatabaseTestBase(fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 10, 0, 0, TimeSpan.Zero);

    private SyncWriter CreateWriter() => new(Db, new GroupSequenceAllocator(Db), new FixedClock(Now));

    [Fact]
    public async Task Recording_a_write_ticks_the_writing_device()
    {
        var (group, expense) = await SeedExpenseAsync();
        var before = expense.Clock[TestData.DeviceB];

        await CreateWriter().RecordAsync(
            expense, SyncEntityType.Expense, group.Id, SyncOperation.Update,
            TestData.DeviceB, null, new { expense.Description });
        await Db.SaveChangesAsync();

        expense.Clock[TestData.DeviceB].ShouldBe(before + 1);
    }

    [Fact]
    public async Task Recording_a_write_leaves_other_devices_alone()
    {
        var (group, expense) = await SeedExpenseAsync();
        var otherDeviceCount = expense.Clock[TestData.DeviceA];

        await CreateWriter().RecordAsync(
            expense, SyncEntityType.Expense, group.Id, SyncOperation.Update,
            TestData.DeviceB, null, new { });
        await Db.SaveChangesAsync();

        expense.Clock[TestData.DeviceA].ShouldBe(otherDeviceCount);
    }

    [Fact]
    public async Task The_entity_records_who_wrote_it_and_when()
    {
        var (group, expense) = await SeedExpenseAsync();

        await CreateWriter().RecordAsync(
            expense, SyncEntityType.Expense, group.Id, SyncOperation.Update,
            TestData.DeviceB, null, new { });
        await Db.SaveChangesAsync();

        var reloaded = await NewContext().Expenses.FirstAsync(e => e.Id == expense.Id);
        reloaded.LastWriterDeviceId.ShouldBe(TestData.DeviceB);
        reloaded.UpdatedAt.ShouldBe(Now);
    }

    [Fact]
    public async Task The_entity_and_its_log_entry_agree_on_the_sequence_number()
    {
        var (group, expense) = await SeedExpenseAsync();

        var seq = await CreateWriter().RecordAsync(
            expense, SyncEntityType.Expense, group.Id, SyncOperation.Update,
            TestData.DeviceA, null, new { });
        await Db.SaveChangesAsync();

        var entry = await NewContext().SyncLog.SingleAsync(e => e.ServerSeq == seq);
        expense.ServerSeq.ShouldBe(seq);
        entry.EntityId.ShouldBe(expense.Id);
        entry.GroupId.ShouldBe(group.Id);
    }

    [Fact]
    public async Task Successive_writes_take_successive_sequence_numbers()
    {
        var (group, expense) = await SeedExpenseAsync();
        var writer = CreateWriter();

        var first = await writer.RecordAsync(expense, SyncEntityType.Expense, group.Id,
            SyncOperation.Update, TestData.DeviceA, null, new { });
        var second = await writer.RecordAsync(expense, SyncEntityType.Expense, group.Id,
            SyncOperation.Update, TestData.DeviceA, null, new { });
        await Db.SaveChangesAsync();

        second.ShouldBe(first + 1);
    }

    [Fact]
    public async Task The_log_entry_carries_the_payload_snapshot()
    {
        var (group, expense) = await SeedExpenseAsync();

        await CreateWriter().RecordAsync(
            expense, SyncEntityType.Expense, group.Id, SyncOperation.Create,
            TestData.DeviceA, null, new { description = "Groceries", amount = 42.5m });
        await Db.SaveChangesAsync();

        var entry = await NewContext().SyncLog.SingleAsync();
        entry.PayloadJson.ShouldContain("Groceries");
        entry.PayloadJson.ShouldContain("42.5");
    }

    [Fact]
    public async Task The_log_entry_carries_the_clock_as_written()
    {
        var (group, expense) = await SeedExpenseAsync();

        await CreateWriter().RecordAsync(
            expense, SyncEntityType.Expense, group.Id, SyncOperation.Update,
            TestData.DeviceB, null, new { });
        await Db.SaveChangesAsync();

        var entry = await NewContext().SyncLog.SingleAsync();
        SplitEverything.Domain.Sync.VectorClock.FromJson(entry.VectorClockJson).ShouldBe(expense.Clock);
    }

    [Fact]
    public async Task The_log_entry_defaults_its_lineage_to_the_group()
    {
        var (group, expense) = await SeedExpenseAsync();

        await CreateWriter().RecordAsync(
            expense, SyncEntityType.Expense, group.Id, SyncOperation.Update,
            TestData.DeviceA, null, new { });
        await Db.SaveChangesAsync();

        (await NewContext().SyncLog.SingleAsync()).LineageId.ShouldBe(group.LineageId);
    }

    [Fact]
    public async Task A_transfer_records_where_the_entity_came_from()
    {
        var (group, expense) = await SeedExpenseAsync();
        var sourceGroupId = Guid.NewGuid();

        await CreateWriter().RecordAsync(
            expense, SyncEntityType.Expense, group.Id, SyncOperation.Transfer,
            TestData.DeviceA, null, new { }, sourceGroupId: sourceGroupId);
        await Db.SaveChangesAsync();

        var entry = await NewContext().SyncLog.SingleAsync();
        entry.Operation.ShouldBe(SyncOperation.Transfer);
        entry.SourceGroupId.ShouldBe(sourceGroupId);
    }

    [Fact]
    public async Task A_transfer_can_keep_the_lineage_of_the_log_it_came_from()
    {
        var (group, expense) = await SeedExpenseAsync();
        var originalLineage = Guid.NewGuid();

        await CreateWriter().RecordAsync(
            expense, SyncEntityType.Expense, group.Id, SyncOperation.Transfer,
            TestData.DeviceA, null, new { }, lineageId: originalLineage);
        await Db.SaveChangesAsync();

        // Keeping the origin lineage is what lets a later split pull the moved
        // history back out again without guessing.
        (await NewContext().SyncLog.SingleAsync()).LineageId.ShouldBe(originalLineage);
    }

    [Fact]
    public async Task The_recording_user_is_attributed_on_the_log_entry()
    {
        var (group, expense) = await SeedExpenseAsync();
        var userId = await Db.Users.Select(u => u.Id).FirstAsync();

        await CreateWriter().RecordAsync(
            expense, SyncEntityType.Expense, group.Id, SyncOperation.Update,
            TestData.DeviceA, userId, new { });
        await Db.SaveChangesAsync();

        (await NewContext().SyncLog.SingleAsync()).UserId.ShouldBe(userId);
    }

    [Fact]
    public async Task Recording_a_delete_marks_the_tombstone_on_the_entity()
    {
        var (group, expense) = await SeedExpenseAsync();

        await CreateWriter().RecordAsync(
            expense, SyncEntityType.Expense, group.Id, SyncOperation.Delete,
            TestData.DeviceA, null, new { });
        await Db.SaveChangesAsync();

        var reloaded = await NewContext().Expenses.FirstAsync(e => e.Id == expense.Id);
        reloaded.IsDeleted.ShouldBeTrue();
        reloaded.DeletedAt.ShouldBe(Now);
    }

    [Fact]
    public async Task A_deleted_row_is_kept_so_offline_peers_can_learn_of_the_delete()
    {
        var (group, expense) = await SeedExpenseAsync();

        await CreateWriter().RecordAsync(
            expense, SyncEntityType.Expense, group.Id, SyncOperation.Delete,
            TestData.DeviceA, null, new { });
        await Db.SaveChangesAsync();

        (await NewContext().Expenses.CountAsync(e => e.Id == expense.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task Writing_to_an_archived_group_is_refused()
    {
        var (group, expense) = await SeedExpenseAsync();
        await Db.Groups.Where(g => g.Id == group.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.IsArchived, true));
        Db.ChangeTracker.Clear();
        var tracked = await Db.Expenses.FirstAsync(e => e.Id == expense.Id);

        await Should.ThrowAsync<Application.Common.GroupArchivedException>(() =>
            CreateWriter().RecordAsync(tracked, SyncEntityType.Expense, group.Id,
                SyncOperation.Update, TestData.DeviceA, null, new { }));
    }

    [Fact]
    public async Task An_archived_group_can_still_be_unarchived()
    {
        var (group, _) = await SeedExpenseAsync();
        await Db.Groups.Where(g => g.Id == group.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.IsArchived, true));
        Db.ChangeTracker.Clear();
        var tracked = await Db.Groups.FirstAsync(g => g.Id == group.Id);

        // Otherwise the archive would be a one-way door: the unarchive write itself
        // targets the archived group.
        var seq = await CreateWriter().RecordAsync(
            tracked, SyncEntityType.Group, group.Id, SyncOperation.Update,
            TestData.DeviceA, null, new { }, allowArchived: true);
        await Db.SaveChangesAsync();

        seq.ShouldBeGreaterThan(0);
    }

    private async Task<(Group Group, Expense Expense)> SeedExpenseAsync()
    {
        var user = await TestData.SeedUserAsync(Db);
        var (group, members) = await TestData.SeedGroupAsync(Db, user, "Alice");
        var expense = TestData.Expense(group.Id, members["Alice"], 50m);
        Db.Expenses.Add(expense);
        await Db.SaveChangesAsync();
        return (group, expense);
    }
}

public sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}
