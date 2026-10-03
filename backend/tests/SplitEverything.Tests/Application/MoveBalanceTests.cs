using Shouldly;
using Microsoft.EntityFrameworkCore;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Expenses;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Application.Contracts.Settlements;
using SplitEverything.Domain.Common;
using SplitEverything.Tests.Support;

namespace SplitEverything.Tests.Application;

public class MoveBalanceTests(PostgresFixture fixture) : ServiceTestBase(fixture)
{
    private async Task<(GroupDto Group, Guid Mine, Guid Theirs)> ShareGroupAsync(
        Guid userId, Guid otherId, string name, string currency = "CAD")
    {
        var group = await Groups.CreateAsync(userId, new CreateGroupRequest(name, currency, null, null, null, null));
        await Groups.AddUserMemberAsync(userId, group.Id, new AddUserMemberRequest(otherId));

        var loaded = await Groups.GetAsync(userId, group.Id);
        return (loaded,
            loaded.Members.First(m => m.UserId == userId).Id,
            loaded.Members.First(m => m.UserId == otherId).Id);
    }

    private Task SpendAsync(Guid userId, Guid groupId, Guid payer, Guid other, decimal amount, string currency = "CAD")
        => Expenses.CreateAsync(userId, new CreateExpenseRequest(
            groupId, payer, "Shared", amount, currency, TestData.Jan1, SplitType.Equal,
            [new SplitInputDto(payer, null), new SplitInputDto(other, null)],
            null, null, null, null, null, null));

    private async Task<(Guid Me, Guid Them, GroupDto Flat, Guid MyFlat, Guid TheirFlat, GroupDto Trip)>
        DebtsFacingTheSameWayAsync()
    {
        var me = await TestData.SeedUserAsync(Db, "Nicolas");
        var them = await TestData.SeedUserAsync(Db, "Emma");
        var (flat, myFlat, theirFlat) = await ShareGroupAsync(me.Id, them.Id, "Colocation");
        var (trip, myTrip, theirTrip) = await ShareGroupAsync(me.Id, them.Id, "Ski trip");

        await SpendAsync(me.Id, flat.Id, theirFlat, myFlat, 140m);
        await SpendAsync(me.Id, trip.Id, theirTrip, myTrip, 60m);

        return (me.Id, them.Id, flat, myFlat, theirFlat, trip);
    }

    [Fact]
    public async Task Moving_a_balance_clears_it_here_and_adds_it_there()
    {
        var (me, _, flat, myFlat, theirFlat, trip) = await DebtsFacingTheSameWayAsync();

        await Settlements.MoveBalanceAsync(me, new MoveBalanceRequest(
            flat.Id, myFlat, theirFlat, 70m, trip.Id, null, null));

        var flatAfter = await Settlements.GetGroupBalanceAsync(me, flat.Id);
        var tripAfter = await Settlements.GetGroupBalanceAsync(me, trip.Id);

        flatAfter.Balances.ShouldAllBe(b => b.Net == 0m);
        tripAfter.Balances.Single(b => b.MemberName == "Nicolas").Net.ShouldBe(-100m);
    }

    [Fact]
    public async Task A_moved_balance_is_one_settlement_in_each_group_tied_together()
    {
        var (me, _, flat, myFlat, theirFlat, trip) = await DebtsFacingTheSameWayAsync();

        var result = await Settlements.MoveBalanceAsync(me, new MoveBalanceRequest(
            flat.Id, myFlat, theirFlat, 70m, trip.Id, null, null));

        var written = await NewContext().Settlements.ToListAsync();
        var inFlat = written.Single(s => s.Id == result.SourceSettlementId);
        var inTrip = written.Single(s => s.Id == result.TargetSettlementId);

        inFlat.GroupId.ShouldBe(flat.Id);
        inFlat.Note.ShouldBe("Moved to Ski trip");
        inTrip.GroupId.ShouldBe(trip.Id);
        inTrip.Note.ShouldBe("Moved from Colocation");
        inFlat.OffsetSettlementId.ShouldBe(inTrip.Id);
        inTrip.OffsetSettlementId.ShouldBe(inFlat.Id);

        var entries = await NewContext().SyncLog.Where(e => e.EntityType == SyncEntityType.Settlement).ToListAsync();
        entries.Select(e => e.GroupId).ShouldBe([flat.Id, trip.Id], ignoreOrder: true);
    }

    [Fact]
    public async Task A_balance_cannot_move_to_a_group_kept_in_another_currency()
    {
        var me = await TestData.SeedUserAsync(Db, "Nicolas");
        var them = await TestData.SeedUserAsync(Db, "Emma");
        var (flat, myFlat, theirFlat) = await ShareGroupAsync(me.Id, them.Id, "Colocation");
        var (spain, _, _) = await ShareGroupAsync(me.Id, them.Id, "Voyage Espagne", "EUR");

        await Should.ThrowAsync<ValidationException>(() => Settlements.MoveBalanceAsync(me.Id,
            new MoveBalanceRequest(flat.Id, myFlat, theirFlat, 10m, spain.Id, null, null)));
    }

    [Fact]
    public async Task A_balance_cannot_move_to_a_group_the_other_person_is_not_in()
    {
        var me = await TestData.SeedUserAsync(Db, "Nicolas");
        var them = await TestData.SeedUserAsync(Db, "Emma");
        var (flat, myFlat, theirFlat) = await ShareGroupAsync(me.Id, them.Id, "Colocation");
        var solo = await Groups.CreateAsync(me.Id, new CreateGroupRequest("Solo", "CAD", null, null, null, null));

        await Should.ThrowAsync<ValidationException>(() => Settlements.MoveBalanceAsync(me.Id,
            new MoveBalanceRequest(flat.Id, myFlat, theirFlat, 10m, solo.Id, null, null)));
    }
}
