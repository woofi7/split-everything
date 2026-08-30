using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Application.Contracts.Sync;
using SplitEverything.Domain.Common;
using SplitEverything.Domain.Entities;
using SplitEverything.Infrastructure.Services;
using SplitEverything.Tests.Support;
using Shouldly;

namespace SplitEverything.Tests.Application;

/// <summary>
/// The activity feed and the way the app actually writes.
///
/// Every screen in the app is offline first, so an expense is queued locally and
/// pushed through the sync endpoint. Only the REST services recorded activity, so
/// nothing anyone did in the app ever reached the feed: it showed group and member
/// events, which do go through REST, and never an expense.
/// </summary>
public class SyncActivityTests(PostgresFixture fixture) : ServiceTestBase(fixture)
{
    private SyncService Sync => new(Db, Writer, Broadcaster, Clock, Activity);

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
            splitType = "Equal",
            splits = splits.Select(s => new { memberId = s.MemberId, amount = s.Amount, amountInBaseCurrency = s.Amount })
        });

    private static SyncOperationDto Operation(
        Guid groupId, SyncEntityType type, Guid entityId, SyncOperation operation, string payload)
        => new(Guid.CreateVersion7(), type, entityId, operation, groupId, payload,
            new Dictionary<string, long> { [TestData.DeviceB] = 1 }, TestData.Jan1);

    private async Task<List<ActivityLogEntry>> ActivityForAsync(Guid groupId)
        => await NewContext().ActivityLog
            .Where(entry => entry.GroupId == groupId)
            .OrderBy(entry => entry.Id)
            .ToListAsync();

    [Fact]
    public async Task An_expense_pushed_from_the_app_reaches_the_feed()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        var expenseId = Guid.CreateVersion7();

        var result = await Sync.PushAsync(userId, new SyncPushRequest(TestData.DeviceB, [
            Operation(group.Id, SyncEntityType.Expense, expenseId, SyncOperation.Create,
                ExpenseJson(expenseId, group.Id, alice, 40m, "Dinner out", [(alice, 20m), (bob, 20m)]))
        ]));

        result.Accepted.ShouldHaveSingleItem();

        var entry = (await ActivityForAsync(group.Id))
            .Where(entry => entry.Kind == ActivityKind.ExpenseCreated)
            .ShouldHaveSingleItem();

        entry.Summary.ShouldContain("Dinner out");
        entry.SubjectId.ShouldBe(expenseId);
        entry.SubjectType.ShouldBe(SyncEntityType.Expense);
    }

    [Fact]
    public async Task The_entry_names_who_added_it()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        var expenseId = Guid.CreateVersion7();

        await Sync.PushAsync(userId, new SyncPushRequest(TestData.DeviceB, [
            Operation(group.Id, SyncEntityType.Expense, expenseId, SyncOperation.Create,
                ExpenseJson(expenseId, group.Id, alice, 40m, "Dinner out", [(alice, 20m), (bob, 20m)]))
        ]));

        var entry = (await ActivityForAsync(group.Id))
            .First(entry => entry.Kind == ActivityKind.ExpenseCreated);

        entry.Summary.ShouldContain("Alice");
    }

    [Fact]
    public async Task Editing_an_expense_from_the_app_reaches_the_feed()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        var expenseId = Guid.CreateVersion7();

        await Sync.PushAsync(userId, new SyncPushRequest(TestData.DeviceB, [
            Operation(group.Id, SyncEntityType.Expense, expenseId, SyncOperation.Create,
                ExpenseJson(expenseId, group.Id, alice, 40m, "Dinner out", [(alice, 20m), (bob, 20m)]))
        ]));

        await Sync.PushAsync(userId, new SyncPushRequest(TestData.DeviceB, [
            new SyncOperationDto(Guid.CreateVersion7(), SyncEntityType.Expense, expenseId,
                SyncOperation.Update, group.Id,
                ExpenseJson(expenseId, group.Id, alice, 50m, "Dinner out, again", [(alice, 25m), (bob, 25m)]),
                new Dictionary<string, long> { [TestData.DeviceB] = 2 }, TestData.Jan1)
        ]));

        (await ActivityForAsync(group.Id))
            .ShouldContain(entry => entry.Kind == ActivityKind.ExpenseUpdated);
    }

    [Fact]
    public async Task Deleting_an_expense_from_the_app_reaches_the_feed()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        var expenseId = Guid.CreateVersion7();

        await Sync.PushAsync(userId, new SyncPushRequest(TestData.DeviceB, [
            Operation(group.Id, SyncEntityType.Expense, expenseId, SyncOperation.Create,
                ExpenseJson(expenseId, group.Id, alice, 40m, "Dinner out", [(alice, 20m), (bob, 20m)]))
        ]));

        await Sync.PushAsync(userId, new SyncPushRequest(TestData.DeviceB, [
            new SyncOperationDto(Guid.CreateVersion7(), SyncEntityType.Expense, expenseId,
                SyncOperation.Delete, group.Id, "{}",
                new Dictionary<string, long> { [TestData.DeviceB] = 2 }, TestData.Jan1)
        ]));

        (await ActivityForAsync(group.Id))
            .ShouldContain(entry => entry.Kind == ActivityKind.ExpenseDeleted);
    }

    [Fact]
    public async Task A_settlement_pushed_from_the_app_reaches_the_feed()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        var settlementId = Guid.CreateVersion7();

        var payload = JsonSerializer.Serialize(new
        {
            id = settlementId,
            groupId = group.Id,
            fromMemberId = bob,
            toMemberId = alice,
            amount = 20m,
            currency = "CAD",
            amountInBaseCurrency = 20m,
            settledAt = TestData.Jan1
        });

        await Sync.PushAsync(userId, new SyncPushRequest(TestData.DeviceB, [
            Operation(group.Id, SyncEntityType.Settlement, settlementId, SyncOperation.Create, payload)
        ]));

        (await ActivityForAsync(group.Id))
            .ShouldContain(entry => entry.Kind == ActivityKind.SettlementCreated);
    }

    [Fact]
    public async Task A_comment_pushed_from_the_app_reaches_the_feed()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        var expenseId = Guid.CreateVersion7();

        await Sync.PushAsync(userId, new SyncPushRequest(TestData.DeviceB, [
            Operation(group.Id, SyncEntityType.Expense, expenseId, SyncOperation.Create,
                ExpenseJson(expenseId, group.Id, alice, 40m, "Dinner out", [(alice, 20m), (bob, 20m)]))
        ]));

        var commentId = Guid.CreateVersion7();
        var payload = JsonSerializer.Serialize(new
        {
            id = commentId,
            expenseId,
            groupId = group.Id,
            authorMemberId = alice,
            body = "Was the taxi in this?"
        });

        await Sync.PushAsync(userId, new SyncPushRequest(TestData.DeviceB, [
            Operation(group.Id, SyncEntityType.ExpenseComment, commentId, SyncOperation.Create, payload)
        ]));

        (await ActivityForAsync(group.Id))
            .ShouldContain(entry => entry.Kind == ActivityKind.CommentPosted);
    }

    [Fact]
    public async Task An_operation_already_applied_does_not_log_twice()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        var expenseId = Guid.CreateVersion7();

        var operation = Operation(group.Id, SyncEntityType.Expense, expenseId, SyncOperation.Create,
            ExpenseJson(expenseId, group.Id, alice, 40m, "Dinner out", [(alice, 20m), (bob, 20m)]));

        await Sync.PushAsync(userId, new SyncPushRequest(TestData.DeviceB, [operation]));
        await Sync.PushAsync(userId, new SyncPushRequest(TestData.DeviceB, [operation]));

        // A retry after a dropped connection is normal, and it must not read as the
        // expense being added twice.
        (await ActivityForAsync(group.Id))
            .Count(entry => entry.Kind == ActivityKind.ExpenseCreated)
            .ShouldBe(1);
    }

    [Fact]
    public async Task A_rejected_operation_logs_nothing()
    {
        var (userId, group, _, _) = await SetupAsync();
        var expenseId = Guid.CreateVersion7();

        await Sync.PushAsync(userId, new SyncPushRequest(TestData.DeviceB, [
            Operation(group.Id, SyncEntityType.Expense, expenseId, SyncOperation.Create, "{ not json")
        ]));

        (await ActivityForAsync(group.Id))
            .ShouldNotContain(entry => entry.Kind == ActivityKind.ExpenseCreated);
    }
}
