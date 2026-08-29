using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Application.Contracts.Sync;
using SplitEverything.Domain.Common;
using SplitEverything.Infrastructure.Services;
using SplitEverything.Infrastructure.Sync;
using SplitEverything.Tests.Support;
using Shouldly;

namespace SplitEverything.Tests.Application;

/// <summary>
/// The sync payload wire format, pinned from the client's side.
///
/// These exist because the rest of the sync suite builds payloads the way the
/// server writes them, which cannot catch a shape only the client sends. The
/// browser holds a split type as its name, so a name has to be readable here or
/// every expense a real client pushes is rejected as unparseable.
/// </summary>
public class SyncPayloadWireFormatTests(PostgresFixture fixture) : ServiceTestBase(fixture)
{
    private SyncService Sync => new(Db, Writer, Broadcaster, Clock);

    private async Task<(Guid UserId, GroupDto Group, Guid Alice)> SetupAsync()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, []));
        return (user.Id, group, group.Members.First(m => m.UserId == user.Id).Id);
    }

    private async Task<SyncPushResult> PushExpenseAsync(
        Guid userId, Guid groupId, Guid payer, object splitType)
    {
        var expenseId = Guid.CreateVersion7();
        var json = JsonSerializer.Serialize(new
        {
            id = expenseId,
            groupId,
            paidByMemberId = payer,
            description = "Dinner",
            amount = 40m,
            currency = "CAD",
            amountInBaseCurrency = 40m,
            exchangeRate = 1m,
            spentAt = TestData.Jan1,
            categoryId = (Guid?)null,
            splitType,
            receiptId = (Guid?)null,
            notes = (string?)null,
            splits = new[]
            {
                new { memberId = payer, amount = 40m, amountInBaseCurrency = 40m, inputValue = (decimal?)null }
            },
            items = Array.Empty<object>()
        });

        return await Sync.PushAsync(userId, new SyncPushRequest(TestData.DeviceB, [
            new SyncOperationDto(Guid.CreateVersion7(), SyncEntityType.Expense, expenseId,
                SyncOperation.Create, groupId, json,
                new Dictionary<string, long> { [TestData.DeviceB] = 1 }, TestData.Jan1)
        ]));
    }

    [Theory]
    [InlineData("Equal", SplitType.Equal)]
    [InlineData("Percentage", SplitType.Percentage)]
    [InlineData("Shares", SplitType.Shares)]
    [InlineData("ExactAmount", SplitType.ExactAmount)]
    [InlineData("Itemized", SplitType.Itemized)]
    public async Task A_split_type_sent_as_its_name_is_accepted(string name, SplitType expected)
    {
        var (userId, group, alice) = await SetupAsync();

        var result = await PushExpenseAsync(userId, group.Id, alice, name);

        result.Rejected.ShouldBeEmpty();
        var accepted = result.Accepted.ShouldHaveSingleItem();

        var stored = await Db.Expenses.SingleAsync(e => e.Id == accepted.EntityId);
        stored.SplitType.ShouldBe(expected);
    }

    [Fact]
    public async Task A_split_type_sent_as_its_number_is_still_accepted()
    {
        // Payloads already in the sync log were written numerically, so a client
        // replaying history must keep working.
        var (userId, group, alice) = await SetupAsync();

        var result = await PushExpenseAsync(userId, group.Id, alice, (int)SplitType.Shares);

        result.Rejected.ShouldBeEmpty();
        var accepted = result.Accepted.ShouldHaveSingleItem();

        var stored = await Db.Expenses.SingleAsync(e => e.Id == accepted.EntityId);
        stored.SplitType.ShouldBe(SplitType.Shares);
    }

    [Fact]
    public async Task A_split_type_that_is_not_a_real_name_is_rejected_not_defaulted()
    {
        var (userId, group, alice) = await SetupAsync();

        var result = await PushExpenseAsync(userId, group.Id, alice, "SomethingElse");

        result.Accepted.ShouldBeEmpty();
        result.Rejected.ShouldHaveSingleItem().Code.ShouldBe("InvalidPayload");
    }

    [Fact]
    public void Every_split_type_name_the_client_can_hold_round_trips()
    {
        // The browser's split type is a string union of exactly these names.
        foreach (var value in Enum.GetValues<SplitType>())
        {
            var json = JsonSerializer.Serialize(new { splitType = value.ToString() });
            var parsed = SyncPayloads.Parse<SyncPayloads.ExpensePayload>(json);

            parsed.ShouldNotBeNull();
            parsed.SplitType.ShouldBe(value);
        }
    }
}
