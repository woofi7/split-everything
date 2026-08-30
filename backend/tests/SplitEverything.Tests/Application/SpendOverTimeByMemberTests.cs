using Microsoft.EntityFrameworkCore;
using SplitEverything.Application.Contracts.Expenses;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Application.Contracts.Stats;
using SplitEverything.Domain.Common;
using SplitEverything.Infrastructure.Services;
using SplitEverything.Tests.Support;
using Shouldly;

namespace SplitEverything.Tests.Application;

/// <summary>
/// Spending over time, split by who paid.
///
/// A single bar per month says how much was spent. Split by person it also says who
/// carried it, which is the thing a shared account argues about, and it costs
/// nothing to compute alongside the total that was already there.
/// </summary>
public class SpendOverTimeByMemberTests(PostgresFixture fixture) : ServiceTestBase(fixture)
{
    private StatsService Stats => new(Db, Currency, Clock);

    private async Task<(Guid UserId, GroupDto Group, Guid Alice, Guid Bob)> SetupAsync()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, ["Bob"]));
        return (user.Id, group,
            group.Members.First(m => m.UserId == user.Id).Id,
            group.Members.First(m => m.DisplayName == "Bob").Id);
    }

    private async Task AddAsync(
        Guid userId, Guid groupId, Guid payer, decimal amount, DateTimeOffset spentAt,
        string description = "Something")
        => await Expenses.CreateAsync(userId, new CreateExpenseRequest(
            groupId, payer, description, amount, "CAD", spentAt, SplitType.Equal,
            [new SplitInputDto(payer, null)], null, null, null, null, null, null));

    [Fact]
    public async Task Each_bucket_names_who_paid_within_it()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        var january = new DateTimeOffset(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);

        await AddAsync(userId, group.Id, alice, 60m, january);
        await AddAsync(userId, group.Id, bob, 40m, january);

        var stats = await Stats.GetDashboardAsync(userId, new StatsQuery(GroupId: group.Id));

        var bucket = stats.SpendOverTime.ShouldHaveSingleItem();
        bucket.Amount.ShouldBe(100m);

        var byMember = bucket.ByMember.ToDictionary(m => m.MemberName, m => m.Amount);
        byMember["Alice"].ShouldBe(60m);
        byMember["Bob"].ShouldBe(40m);
    }

    [Fact]
    public async Task The_parts_add_up_to_the_bucket()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        var january = new DateTimeOffset(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);

        await AddAsync(userId, group.Id, alice, 33.33m, january);
        await AddAsync(userId, group.Id, bob, 33.34m, january);

        var bucket = (await Stats.GetDashboardAsync(userId, new StatsQuery(GroupId: group.Id)))
            .SpendOverTime.ShouldHaveSingleItem();

        // A stacked bar whose parts do not sum to its total is a lie about both.
        bucket.ByMember.Sum(m => m.Amount).ShouldBe(bucket.Amount);
    }

    [Fact]
    public async Task Someone_who_paid_nothing_in_a_bucket_is_left_out_of_it()
    {
        var (userId, group, alice, _) = await SetupAsync();
        var january = new DateTimeOffset(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);

        await AddAsync(userId, group.Id, alice, 60m, january);

        var bucket = (await Stats.GetDashboardAsync(userId, new StatsQuery(GroupId: group.Id)))
            .SpendOverTime.ShouldHaveSingleItem();

        // A zero-height segment is not information.
        bucket.ByMember.ShouldHaveSingleItem().MemberName.ShouldBe("Alice");
    }

    [Fact]
    public async Task Buckets_keep_their_own_people()
    {
        var (userId, group, alice, bob) = await SetupAsync();

        await AddAsync(userId, group.Id, alice, 60m, new DateTimeOffset(2026, 1, 10, 12, 0, 0, TimeSpan.Zero));
        await AddAsync(userId, group.Id, bob, 40m, new DateTimeOffset(2026, 2, 10, 12, 0, 0, TimeSpan.Zero));

        var buckets = (await Stats.GetDashboardAsync(userId, new StatsQuery(GroupId: group.Id)))
            .SpendOverTime.ToList();

        buckets.Count.ShouldBe(2);
        buckets[0].ByMember.ShouldHaveSingleItem().MemberName.ShouldBe("Alice");
        buckets[1].ByMember.ShouldHaveSingleItem().MemberName.ShouldBe("Bob");
    }

    [Fact]
    public async Task The_biggest_payer_comes_first_in_a_bucket()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        var january = new DateTimeOffset(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);

        await AddAsync(userId, group.Id, bob, 10m, january);
        await AddAsync(userId, group.Id, alice, 90m, january);

        var bucket = (await Stats.GetDashboardAsync(userId, new StatsQuery(GroupId: group.Id)))
            .SpendOverTime.ShouldHaveSingleItem();

        // Stable ordering, so a stack does not reshuffle its own colours between
        // one bucket and the next.
        bucket.ByMember.First().MemberName.ShouldBe("Alice");
    }

    [Fact]
    public async Task A_bucket_carries_the_member_ids_so_a_colour_can_be_chosen()
    {
        var (userId, group, alice, _) = await SetupAsync();

        await AddAsync(userId, group.Id, alice, 60m, new DateTimeOffset(2026, 1, 10, 12, 0, 0, TimeSpan.Zero));

        var bucket = (await Stats.GetDashboardAsync(userId, new StatsQuery(GroupId: group.Id)))
            .SpendOverTime.ShouldHaveSingleItem();

        // The client colours people by id, so the name alone is not enough.
        bucket.ByMember.ShouldHaveSingleItem().MemberId.ShouldBe(alice);
    }

    [Fact]
    public async Task Works_across_groups_too()
    {
        var (userId, group, alice, _) = await SetupAsync();
        var second = await Groups.CreateAsync(userId,
            new CreateGroupRequest("Ski trip", "CAD", null, null, null, []));
        var meThere = second.Members.First(m => m.UserId == userId).Id;
        var january = new DateTimeOffset(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);

        await AddAsync(userId, group.Id, alice, 60m, january);
        await AddAsync(userId, second.Id, meThere, 40m, january);

        var bucket = (await Stats.GetDashboardAsync(userId, new StatsQuery()))
            .SpendOverTime.ShouldHaveSingleItem();

        bucket.Amount.ShouldBe(100m);
        // One person, two groups, two member rows: they are different memberships.
        bucket.ByMember.Sum(m => m.Amount).ShouldBe(100m);
    }
}
