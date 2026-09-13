using Shouldly;
using Microsoft.EntityFrameworkCore;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Expenses;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Application.Contracts.Settlements;
using SplitEverything.Domain.Common;
using SplitEverything.Tests.Support;

namespace SplitEverything.Tests.Application;

/// <summary>
/// Two people who share more than one group can owe each other in both directions
/// at once: a thousand one way in the flat, nine hundred the other way on a trip.
/// Paying both in full is two transfers where none is needed, so the halves that
/// face each other are cancelled and only the difference is left standing.
/// </summary>
public class CrossGroupSettlementTests(PostgresFixture fixture) : ServiceTestBase(fixture)
{
    /// <summary>A group the two of them are both real members of.</summary>
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

    /// <summary>`payer` covers an expense split evenly between the two of them.</summary>
    private Task SpendAsync(Guid userId, Guid groupId, Guid payer, Guid other, decimal amount, string currency = "CAD")
        => Expenses.CreateAsync(userId, new CreateExpenseRequest(
            groupId, payer, "Shared", amount, currency, TestData.Jan1, SplitType.Equal,
            [new SplitInputDto(payer, null), new SplitInputDto(other, null)],
            null, null, null, null, null, null));

    /// <summary>
    /// The case this exists for: Emma owes 1025 in the flat, Nicolas owes 925 on
    /// the trip, and between them only 100 is really outstanding.
    /// </summary>
    private async Task<(Guid Me, Guid Them, GroupDto Flat, GroupDto Trip)> FacingDebtsAsync()
    {
        var me = await TestData.SeedUserAsync(Db, "Nicolas");
        var them = await TestData.SeedUserAsync(Db, "Emma");

        var (flat, myFlatId, theirFlatId) = await ShareGroupAsync(me.Id, them.Id, "Colocation");
        var (trip, myTripId, theirTripId) = await ShareGroupAsync(me.Id, them.Id, "Ski trip");

        // I paid 2050 in the flat, so they owe me half of it.
        await SpendAsync(me.Id, flat.Id, myFlatId, theirFlatId, 2050m);
        // They paid 1850 on the trip, so I owe them half of that.
        await SpendAsync(me.Id, trip.Id, theirTripId, myTripId, 1850m);

        return (me.Id, them.Id, flat, trip);
    }

    [Fact]
    public async Task The_balance_names_every_group_the_two_of_them_share()
    {
        var (me, them, flat, trip) = await FacingDebtsAsync();

        var balance = await Settlements.GetCrossGroupBalanceAsync(me, them);

        balance.WithName.ShouldBe("Emma");
        balance.Groups.Count.ShouldBe(2);
        balance.Groups.First(g => g.GroupId == flat.Id).Net.ShouldBe(1025m);
        balance.Groups.First(g => g.GroupId == trip.Id).Net.ShouldBe(-925m);
    }

    [Fact]
    public async Task It_plans_to_cancel_the_smaller_of_the_two()
    {
        var (me, them, flat, trip) = await FacingDebtsAsync();

        var balance = await Settlements.GetCrossGroupBalanceAsync(me, them);

        var offset = balance.Offsets.ShouldHaveSingleItem();
        offset.Amount.ShouldBe(925m);
        offset.OwedGroupId.ShouldBe(flat.Id);
        offset.OwingGroupId.ShouldBe(trip.Id);

        var remaining = balance.Remaining.ShouldHaveSingleItem();
        remaining.Net.ShouldBe(100m);
        remaining.GroupName.ShouldBe("Colocation");
    }

    [Fact]
    public async Task Cancelling_leaves_only_the_difference_and_leaves_it_in_one_place()
    {
        var (me, them, flat, trip) = await FacingDebtsAsync();

        await Settlements.OffsetAcrossGroupsAsync(me, new OffsetAcrossGroupsRequest(them, null));

        var after = await Settlements.GetCrossGroupBalanceAsync(me, them);

        // The trip is square and the flat carries what was really outstanding.
        after.Groups.Count.ShouldBe(1);
        after.Groups.ShouldHaveSingleItem().GroupId.ShouldBe(flat.Id);
        after.Groups[0].Net.ShouldBe(100m);
        after.Offsets.ShouldBeEmpty();

        var tripBalance = await Settlements.GetGroupBalanceAsync(me, trip.Id);
        tripBalance.Balances.ShouldAllBe(b => b.Net == 0m);
    }

    [Fact]
    public async Task Cancelling_moves_no_money_between_the_two_people()
    {
        var (me, them, _, _) = await FacingDebtsAsync();

        var before = (await Settlements.GetCrossGroupBalanceAsync(me, them)).Groups.Sum(g => g.Net);
        await Settlements.OffsetAcrossGroupsAsync(me, new OffsetAcrossGroupsRequest(them, null));
        var after = (await Settlements.GetCrossGroupBalanceAsync(me, them)).Groups.Sum(g => g.Net);

        // The whole point: the total between the two of them is untouched. A pair
        // that changed it would be a payment nobody made.
        after.ShouldBe(before);
    }

    [Fact]
    public async Task It_writes_one_settlement_in_each_group_and_ties_them_together()
    {
        var (me, them, flat, trip) = await FacingDebtsAsync();

        var result = await Settlements.OffsetAcrossGroupsAsync(me, new OffsetAcrossGroupsRequest(them, null));

        result.SettlementsRecorded.ShouldBe(2);

        var written = await NewContext().Settlements.Where(s => !s.IsDeleted).ToListAsync();
        written.Count.ShouldBe(2);
        written.Select(s => s.GroupId).ShouldBe([flat.Id, trip.Id], ignoreOrder: true);
        written.ShouldAllBe(s => s.Amount == 925m);

        // Each half names the other, so neither can be read - or removed - alone.
        var one = written[0];
        var other = written[1];
        one.OffsetSettlementId.ShouldBe(other.Id);
        other.OffsetSettlementId.ShouldBe(one.Id);
    }

    [Fact]
    public async Task Each_half_says_on_its_face_what_it_is_and_which_group_it_came_from()
    {
        var (me, them, flat, _) = await FacingDebtsAsync();

        await Settlements.OffsetAcrossGroupsAsync(me, new OffsetAcrossGroupsRequest(them, null));

        var inFlat = await NewContext().Settlements.FirstAsync(s => s.GroupId == flat.Id);

        // A settlement nobody paid has to explain itself in the list it appears in.
        inFlat.Note.ShouldBe("Cancelled against Ski trip");
    }

    [Fact]
    public async Task Both_groups_record_it_in_their_own_activity_feed()
    {
        var (me, them, flat, trip) = await FacingDebtsAsync();

        await Settlements.OffsetAcrossGroupsAsync(me, new OffsetAcrossGroupsRequest(them, null));

        var feed = await NewContext().ActivityLog
            .Where(a => a.Kind == ActivityKind.SettlementCreated)
            .ToListAsync();

        feed.Select(a => a.GroupId).ShouldBe([flat.Id, trip.Id], ignoreOrder: true);
    }

    [Fact]
    public async Task Both_devices_hear_about_it_through_the_sync_log()
    {
        var (me, them, flat, trip) = await FacingDebtsAsync();

        await Settlements.OffsetAcrossGroupsAsync(me, new OffsetAcrossGroupsRequest(them, null));

        var entries = await NewContext().SyncLog
            .Where(e => e.EntityType == SyncEntityType.Settlement)
            .ToListAsync();

        // One per group. Written to the log or the phones never learn of a balance
        // that changed underneath them.
        entries.Select(e => e.GroupId).ShouldBe([flat.Id, trip.Id], ignoreOrder: true);
    }

    [Fact]
    public async Task Debts_that_face_the_same_way_are_left_alone()
    {
        var me = await TestData.SeedUserAsync(Db, "Nicolas");
        var them = await TestData.SeedUserAsync(Db, "Emma");
        var (flat, myFlat, theirFlat) = await ShareGroupAsync(me.Id, them.Id, "Colocation");
        var (trip, myTrip, theirTrip) = await ShareGroupAsync(me.Id, them.Id, "Ski trip");

        await SpendAsync(me.Id, flat.Id, myFlat, theirFlat, 100m);
        await SpendAsync(me.Id, trip.Id, myTrip, theirTrip, 60m);

        var balance = await Settlements.GetCrossGroupBalanceAsync(me.Id, them.Id);
        balance.Offsets.ShouldBeEmpty();

        // Nothing cancels, so there is nothing to do, and saying so is better than
        // writing a pair of settlements that change nothing.
        await Should.ThrowAsync<ValidationException>(() =>
            Settlements.OffsetAcrossGroupsAsync(me.Id, new OffsetAcrossGroupsRequest(them.Id, null)));
    }

    [Fact]
    public async Task Groups_kept_in_different_currencies_are_never_cancelled_against_each_other()
    {
        var me = await TestData.SeedUserAsync(Db, "Nicolas");
        var them = await TestData.SeedUserAsync(Db, "Emma");
        var (flat, myFlat, theirFlat) = await ShareGroupAsync(me.Id, them.Id, "Colocation");
        var (spain, mySpain, theirSpain) = await ShareGroupAsync(me.Id, them.Id, "Voyage Espagne", "EUR");

        await SpendAsync(me.Id, flat.Id, myFlat, theirFlat, 200m);
        await SpendAsync(me.Id, spain.Id, theirSpain, mySpain, 200m, "EUR");

        var balance = await Settlements.GetCrossGroupBalanceAsync(me.Id, them.Id);

        // A hundred dollars against a hundred euros is an exchange, not an offset,
        // and picking the rate would commit both people to it.
        balance.Groups.Count.ShouldBe(2);
        balance.Offsets.ShouldBeEmpty();
        balance.Remaining.Count.ShouldBe(2);
    }

    [Fact]
    public async Task An_archived_group_is_shown_but_never_written_to()
    {
        var (me, them, flat, trip) = await FacingDebtsAsync();
        await Groups.ArchiveAsync(me, trip.Id);

        var balance = await Settlements.GetCrossGroupBalanceAsync(me, them);

        balance.Groups.First(g => g.GroupId == trip.Id).CanSettle.ShouldBeFalse();
        balance.Offsets.ShouldBeEmpty();
    }

    [Fact]
    public async Task Somebody_with_no_group_in_common_has_nothing_to_settle()
    {
        var me = await TestData.SeedUserAsync(Db, "Nicolas");
        var stranger = await TestData.SeedUserAsync(Db, "Someone else");

        var balance = await Settlements.GetCrossGroupBalanceAsync(me.Id, stranger.Id);

        balance.Groups.ShouldBeEmpty();
        balance.Offsets.ShouldBeEmpty();
    }

    [Fact]
    public async Task Settling_with_yourself_is_refused()
    {
        var me = await TestData.SeedUserAsync(Db, "Nicolas");

        await Should.ThrowAsync<ValidationException>(() =>
            Settlements.GetCrossGroupBalanceAsync(me.Id, me.Id));
    }

    [Fact]
    public async Task Three_groups_cancel_down_to_one_outstanding_balance()
    {
        var me = await TestData.SeedUserAsync(Db, "Nicolas");
        var them = await TestData.SeedUserAsync(Db, "Emma");
        var (flat, myFlat, theirFlat) = await ShareGroupAsync(me.Id, them.Id, "Colocation");
        var (trip, myTrip, theirTrip) = await ShareGroupAsync(me.Id, them.Id, "Ski trip");
        var (lunch, myLunch, theirLunch) = await ShareGroupAsync(me.Id, them.Id, "Bureau lunches");

        await SpendAsync(me.Id, flat.Id, myFlat, theirFlat, 600m);     // they owe me 300
        await SpendAsync(me.Id, trip.Id, theirTrip, myTrip, 400m);     // I owe them 200
        await SpendAsync(me.Id, lunch.Id, theirLunch, myLunch, 160m);  // I owe them 80

        await Settlements.OffsetAcrossGroupsAsync(me.Id, new OffsetAcrossGroupsRequest(them.Id, null));

        var after = await Settlements.GetCrossGroupBalanceAsync(me.Id, them.Id);

        after.Groups.ShouldHaveSingleItem().Net.ShouldBe(20m);
        after.Groups[0].GroupName.ShouldBe("Colocation");
    }
}
