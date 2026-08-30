using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Expenses;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Application.Contracts.Settlements;
using SplitEverything.Domain.Common;
using SplitEverything.Tests.Support;

namespace SplitEverything.Tests.Application;

public class SettlementServiceTests(PostgresFixture fixture) : ServiceTestBase(fixture)
{
    private async Task<(Guid UserId, GroupDto Group, Guid Alice, Guid Bob, Guid Carol)> SetupAsync()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, ["Bob", "Carol"]));
        return (user.Id, group,
            group.Members.First(m => m.UserId == user.Id).Id,
            group.Members.First(m => m.DisplayName == "Bob").Id,
            group.Members.First(m => m.DisplayName == "Carol").Id);
    }

    private async Task AddExpenseAsync(Guid userId, Guid groupId, Guid payer, decimal amount, params Guid[] participants)
        => await Expenses.CreateAsync(userId, new CreateExpenseRequest(
            groupId, payer, "Shared", amount, "CAD", TestData.Jan1, SplitType.Equal,
            participants.Select(p => new SplitInputDto(p, null)).ToList(), null, null, null, null, null, null));

    [Fact]
    public async Task Recording_a_settlement_clears_the_debt_it_pays()
    {
        var (userId, group, alice, bob, _) = await SetupAsync();
        await AddExpenseAsync(userId, group.Id, alice, 100m, alice, bob);

        await Settlements.CreateAsync(userId, new CreateSettlementRequest(
            group.Id, bob, alice, 50m, "CAD", TestData.Jan1, null, null, null));

        var balance = await Settlements.GetGroupBalanceAsync(userId, group.Id);
        balance.Balances.ShouldAllBe(b => b.Net == 0m);
    }

    [Fact]
    public async Task A_settlement_shows_both_member_names()
    {
        var (userId, group, alice, bob, _) = await SetupAsync();

        var settlement = await Settlements.CreateAsync(userId, new CreateSettlementRequest(
            group.Id, bob, alice, 25m, "CAD", TestData.Jan1, "Etransfer", null, null));

        settlement.FromMemberName.ShouldBe("Bob");
        settlement.ToMemberName.ShouldBe("Alice");
        settlement.Note.ShouldBe("Etransfer");
    }

    [Fact]
    public async Task A_settlement_needs_a_positive_amount()
    {
        var (userId, group, alice, bob, _) = await SetupAsync();

        await Should.ThrowAsync<ValidationException>(() => Settlements.CreateAsync(userId,
            new CreateSettlementRequest(group.Id, bob, alice, 0m, "CAD", TestData.Jan1, null, null, null)));
    }

    [Fact]
    public async Task Nobody_can_settle_with_themselves()
    {
        var (userId, group, alice, _, _) = await SetupAsync();

        await Should.ThrowAsync<ValidationException>(() => Settlements.CreateAsync(userId,
            new CreateSettlementRequest(group.Id, alice, alice, 10m, "CAD", TestData.Jan1, null, null, null)));
    }

    [Fact]
    public async Task Both_sides_of_a_settlement_must_be_group_members()
    {
        var (userId, group, alice, _, _) = await SetupAsync();

        await Should.ThrowAsync<ValidationException>(() => Settlements.CreateAsync(userId,
            new CreateSettlementRequest(group.Id, Guid.NewGuid(), alice, 10m, "CAD", TestData.Jan1, null, null, null)));
    }

    [Fact]
    public async Task A_settlement_in_another_currency_is_converted()
    {
        var (userId, group, alice, bob, _) = await SetupAsync();
        Currency.ConvertAsync(50m, "USD", "CAD", Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ConversionResult(68m, 1.36m, Clock.UtcNow)));

        var settlement = await Settlements.CreateAsync(userId, new CreateSettlementRequest(
            group.Id, bob, alice, 50m, "USD", TestData.Jan1, null, null, null));

        settlement.AmountInBaseCurrency.ShouldBe(68m);
    }

    [Fact]
    public async Task Recording_a_settlement_writes_the_log_and_the_feed()
    {
        var (userId, group, alice, bob, _) = await SetupAsync();

        var settlement = await Settlements.CreateAsync(userId, new CreateSettlementRequest(
            group.Id, bob, alice, 25m, "CAD", TestData.Jan1, null, null, null));

        var fresh = NewContext();
        (await fresh.SyncLog.AnyAsync(e => e.EntityId == settlement.Id)).ShouldBeTrue();
        (await fresh.ActivityLog.AnyAsync(a => a.Kind == ActivityKind.SettlementCreated)).ShouldBeTrue();
    }

    [Fact]
    public async Task Replaying_a_settlement_with_the_same_client_id_does_not_double_pay()
    {
        var (userId, group, alice, bob, _) = await SetupAsync();
        var request = new CreateSettlementRequest(
            group.Id, bob, alice, 25m, "CAD", TestData.Jan1, null, null, Guid.NewGuid());

        var first = await Settlements.CreateAsync(userId, request);
        var second = await Settlements.CreateAsync(userId, request);

        second.Id.ShouldBe(first.Id);
        (await NewContext().Settlements.CountAsync(s => s.GroupId == group.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task Deleting_a_settlement_restores_the_debt()
    {
        var (userId, group, alice, bob, _) = await SetupAsync();
        await AddExpenseAsync(userId, group.Id, alice, 100m, alice, bob);
        var settlement = await Settlements.CreateAsync(userId, new CreateSettlementRequest(
            group.Id, bob, alice, 50m, "CAD", TestData.Jan1, null, null, null));

        await Settlements.DeleteAsync(userId, settlement.Id);

        var balance = await Settlements.GetGroupBalanceAsync(userId, group.Id);
        balance.Balances.First(b => b.MemberId == bob).Net.ShouldBe(-50m);
    }

    [Fact]
    public async Task Listing_returns_settlements_newest_first()
    {
        var (userId, group, alice, bob, _) = await SetupAsync();
        await Settlements.CreateAsync(userId, new CreateSettlementRequest(
            group.Id, bob, alice, 10m, "CAD", TestData.Jan1, "Older", null, null));
        await Settlements.CreateAsync(userId, new CreateSettlementRequest(
            group.Id, bob, alice, 20m, "CAD", TestData.Jan1.AddDays(3), "Newer", null, null));

        var list = await Settlements.ListAsync(userId, group.Id);

        list.Count.ShouldBe(2);
        list[0].Note.ShouldBe("Newer");
    }

    [Fact]
    public async Task The_group_balance_offers_a_simplified_plan()
    {
        var (userId, group, alice, bob, carol) = await SetupAsync();
        // Alice pays 90 for everyone, Bob pays 30 for everyone.
        await AddExpenseAsync(userId, group.Id, alice, 90m, alice, bob, carol);
        await AddExpenseAsync(userId, group.Id, bob, 30m, alice, bob, carol);

        var balance = await Settlements.GetGroupBalanceAsync(userId, group.Id);

        balance.SimplifiedTransfers.Count.ShouldBeLessThanOrEqualTo(2);
        balance.Balances.Sum(b => b.Net).ShouldBe(0m);
        balance.SimplifiedTransfers.ShouldAllBe(t => t.Amount > 0m);
    }

    [Fact]
    public async Task The_simplified_plan_settles_everyone_when_applied()
    {
        var (userId, group, alice, bob, carol) = await SetupAsync();
        await AddExpenseAsync(userId, group.Id, alice, 100m, alice, bob, carol);
        await AddExpenseAsync(userId, group.Id, carol, 25m, alice, bob);

        var balance = await Settlements.GetGroupBalanceAsync(userId, group.Id);
        foreach (var transfer in balance.SimplifiedTransfers)
        {
            await Settlements.CreateAsync(userId, new CreateSettlementRequest(
                group.Id, transfer.FromMemberId, transfer.ToMemberId, transfer.Amount,
                "CAD", TestData.Jan1, "Settled up", null, null));
        }

        var after = await Settlements.GetGroupBalanceAsync(userId, group.Id);
        after.Balances.ShouldAllBe(b => Math.Abs(b.Net) < 0.01m);
    }

    [Fact]
    public async Task The_group_balance_also_shows_the_raw_pairwise_view()
    {
        var (userId, group, alice, bob, _) = await SetupAsync();
        await AddExpenseAsync(userId, group.Id, alice, 100m, alice, bob);

        var balance = await Settlements.GetGroupBalanceAsync(userId, group.Id);

        var debt = balance.PairwiseDebts.ShouldHaveSingleItem();
        debt.FromMemberId.ShouldBe(bob);
        debt.ToMemberId.ShouldBe(alice);
        debt.Amount.ShouldBe(50m);
    }

    [Fact]
    public async Task A_settled_group_needs_no_transfers()
    {
        var (userId, group, _, _, _) = await SetupAsync();

        (await Settlements.GetGroupBalanceAsync(userId, group.Id)).SimplifiedTransfers.ShouldBeEmpty();
    }

    [Fact]
    public async Task Reading_the_balance_of_a_group_you_are_not_in_is_forbidden()
    {
        var (_, group, _, _, _) = await SetupAsync();
        var stranger = await TestData.SeedUserAsync(Db, "Stranger");

        await Should.ThrowAsync<ForbiddenException>(
            () => Settlements.GetGroupBalanceAsync(stranger.Id, group.Id));
    }

    [Fact]
    public async Task The_overall_balance_sums_every_group()
    {
        var (userId, first, alice, bob, _) = await SetupAsync();
        await AddExpenseAsync(userId, first.Id, alice, 100m, alice, bob);

        var second = await Groups.CreateAsync(userId,
            new CreateGroupRequest("Trip", "CAD", null, null, null, ["Dan"]));
        var me = second.Members.First(m => m.UserId == userId).Id;
        var dan = second.Members.First(m => m.DisplayName == "Dan").Id;
        await AddExpenseAsync(userId, second.Id, dan, 40m, me, dan);

        var overall = await Settlements.GetOverallBalanceAsync(userId);

        overall.TotalOwedToMe.ShouldBe(50m);
        overall.TotalIOwe.ShouldBe(20m);
        overall.Net.ShouldBe(30m);
        overall.ByGroup.Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_overall_balance_converts_other_currencies_into_the_users_own()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id,
            new CreateGroupRequest("Euro trip", "EUR", null, null, null, ["Bob"]));
        var me = group.Members.First(m => m.UserId == user.Id).Id;
        var bob = group.Members.First(m => m.DisplayName == "Bob").Id;
        await AddEuroExpenseAsync(user.Id, group.Id, me, 100m, me, bob);

        Currency.ConvertAsync(50m, "EUR", "CAD", Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ConversionResult(74m, 1.48m, Clock.UtcNow)));

        var overall = await Settlements.GetOverallBalanceAsync(user.Id);

        overall.Currency.ShouldBe("CAD");
        overall.ByGroup.ShouldHaveSingleItem().NetInUserCurrency.ShouldBe(74m);
    }

    [Fact]
    public async Task The_overall_balance_ignores_archived_groups()
    {
        var (userId, group, alice, bob, _) = await SetupAsync();
        await AddExpenseAsync(userId, group.Id, alice, 100m, alice, bob);
        await Groups.ArchiveAsync(userId, group.Id);

        (await Settlements.GetOverallBalanceAsync(userId)).ByGroup.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_user_in_no_groups_has_a_zero_overall_balance()
    {
        var user = await TestData.SeedUserAsync(Db);

        var overall = await Settlements.GetOverallBalanceAsync(user.Id);

        overall.Net.ShouldBe(0m);
        overall.ByGroup.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_nudge_pushes_a_reminder_to_the_person_who_owes()
    {
        var owner = await TestData.SeedUserAsync(Db, "Alice");
        var debtor = await TestData.SeedUserAsync(Db, "Bob");
        var group = await Groups.CreateAsync(owner.Id, new CreateGroupRequest("Roommates", "CAD", null, null, null, null));
        var bobMember = TestData.Member(group.Id, debtor.Id, "Bob");
        Db.GroupMembers.Add(bobMember);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var alice = group.Members.First(m => m.UserId == owner.Id).Id;
        await AddExpenseAsync(owner.Id, group.Id, alice, 100m, alice, bobMember.Id);

        await Settlements.NudgeAsync(owner.Id, new NudgeRequest(group.Id, bobMember.Id, "Rent is due"));

        await Push.Received().SendToUsersAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(debtor.Id)),
            Arg.Any<PushMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Nudging_someone_who_owes_nothing_is_rejected()
    {
        var (userId, group, _, bob, _) = await SetupAsync();

        await Should.ThrowAsync<ValidationException>(() => Settlements.NudgeAsync(
            userId, new NudgeRequest(group.Id, bob, null)));
    }

    [Fact]
    public async Task Nudging_records_the_reminder_in_the_feed()
    {
        var owner = await TestData.SeedUserAsync(Db, "Alice");
        var debtor = await TestData.SeedUserAsync(Db, "Bob");
        var group = await Groups.CreateAsync(owner.Id, new CreateGroupRequest("Roommates", "CAD", null, null, null, null));
        var bobMember = TestData.Member(group.Id, debtor.Id, "Bob");
        Db.GroupMembers.Add(bobMember);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        var alice = group.Members.First(m => m.UserId == owner.Id).Id;
        await AddExpenseAsync(owner.Id, group.Id, alice, 60m, alice, bobMember.Id);

        await Settlements.NudgeAsync(owner.Id, new NudgeRequest(group.Id, bobMember.Id, null));

        (await NewContext().ActivityLog.AnyAsync(a => a.Kind == ActivityKind.DebtNudge)).ShouldBeTrue();
    }

    private async Task AddEuroExpenseAsync(Guid userId, Guid groupId, Guid payer, decimal amount, params Guid[] participants)
        => await Expenses.CreateAsync(userId, new CreateExpenseRequest(
            groupId, payer, "Shared", amount, "EUR", TestData.Jan1, SplitType.Equal,
            participants.Select(p => new SplitInputDto(p, null)).ToList(), null, null, null, null, null, null));
}
