using NSubstitute;
using Shouldly;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Expenses;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Application.Contracts.Stats;
using SplitEverything.Domain.Common;
using SplitEverything.Infrastructure.Services;
using SplitEverything.Tests.Support;

namespace SplitEverything.Tests.Application;

public class StatsServiceTests(PostgresFixture fixture) : ServiceTestBase(fixture)
{
    private StatsService Stats { get; set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Stats = new StatsService(Db, Currency);
    }

    private async Task<(Guid UserId, GroupDto Group, Guid Alice, Guid Bob)> SetupAsync()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, ["Bob"]));
        return (user.Id, group,
            group.Members.First(m => m.UserId == user.Id).Id,
            group.Members.First(m => m.DisplayName == "Bob").Id);
    }

    private Task AddAsync(
        Guid userId, Guid groupId, Guid payer, decimal amount,
        DateTimeOffset spentAt, string? categoryKey, params Guid[] participants)
        => Expenses.CreateAsync(userId, new CreateExpenseRequest(
            groupId, payer, "Expense", amount, "CAD", spentAt, SplitType.Equal,
            participants.Select(p => new SplitInputDto(p, null)).ToList(),
            null, null, null, null, null, null, null, categoryKey));

    [Fact]
    public async Task An_empty_group_reports_zeros()
    {
        var (userId, group, _, _) = await SetupAsync();

        var dashboard = await Stats.GetDashboardAsync(userId, new StatsQuery(GroupId: group.Id));

        dashboard.TotalSpend.ShouldBe(0m);
        dashboard.ExpenseCount.ShouldBe(0);
        dashboard.SpendOverTime.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_dashboard_totals_the_spend_and_my_share()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        await AddAsync(userId, group.Id, alice, 100m, TestData.Jan1, "groceries", alice, bob);

        var dashboard = await Stats.GetDashboardAsync(userId, new StatsQuery(GroupId: group.Id));

        dashboard.TotalSpend.ShouldBe(100m);
        dashboard.MyShare.ShouldBe(50m);
        dashboard.MyPaid.ShouldBe(100m);
        dashboard.ExpenseCount.ShouldBe(1);
    }

    /// <summary>
    /// Each bucket cut by what it went on, not only by who paid.
    ///
    /// This is what the chart draws a category's line from. It is held to the same
    /// rules as the payer split beside it - largest first, nothing for a category
    /// that spent nothing, and the parts adding up to the bucket - because the line
    /// is drawn against the bar's own height and a category that was the whole
    /// bucket has to reach the top of it.
    /// </summary>
    [Fact]
    public async Task Each_bucket_says_what_it_went_on()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        await AddAsync(userId, group.Id, alice, 60m, TestData.Jan1, "groceries", alice, bob);
        await AddAsync(userId, group.Id, alice, 25m, TestData.Jan1.AddDays(2), "dining", alice, bob);
        await AddAsync(userId, group.Id, alice, 10m, TestData.Jan1.AddDays(3), null, alice, bob);
        await AddAsync(userId, group.Id, alice, 40m, TestData.Jan1.AddMonths(1), "groceries", alice, bob);

        var dashboard = await Stats.GetDashboardAsync(userId, new StatsQuery(GroupId: group.Id));

        var january = dashboard.SpendOverTime[0].ByCategory!;
        january.ShouldBe([
            new SpendPointCategoryDto("groceries", 60m),
            new SpendPointCategoryDto("dining", 25m),
            // What nobody filed is in the bucket too, for the same reason it is in
            // the breakdown: a line that quietly omits part of the money is worse
            // than one that admits to it.
            new SpendPointCategoryDto(null, 10m),
        ]);

        january.Sum(c => c.Amount).ShouldBe(dashboard.SpendOverTime[0].Amount);

        dashboard.SpendOverTime[1].ByCategory!.ShouldHaveSingleItem()
            .ShouldBe(new SpendPointCategoryDto("groceries", 40m));
    }

    [Fact]
    public async Task Spending_over_time_buckets_by_month_by_default()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        await AddAsync(userId, group.Id, alice, 100m, TestData.Jan1, null, alice, bob);
        await AddAsync(userId, group.Id, alice, 50m, TestData.Jan1.AddDays(10), null, alice, bob);
        await AddAsync(userId, group.Id, alice, 25m, TestData.Jan1.AddMonths(1), null, alice, bob);

        var dashboard = await Stats.GetDashboardAsync(userId, new StatsQuery(GroupId: group.Id));

        dashboard.SpendOverTime.Count.ShouldBe(2);
        dashboard.SpendOverTime[0].Amount.ShouldBe(150m);
        dashboard.SpendOverTime[0].ExpenseCount.ShouldBe(2);
        dashboard.SpendOverTime[1].Amount.ShouldBe(25m);
    }

    [Theory]
    [InlineData("day", 3)]
    [InlineData("week", 2)]
    [InlineData("month", 1)]
    public async Task Spending_over_time_honours_the_requested_granularity(string granularity, int expected)
    {
        var (userId, group, alice, bob) = await SetupAsync();
        await AddAsync(userId, group.Id, alice, 10m, TestData.Jan1, null, alice, bob);
        await AddAsync(userId, group.Id, alice, 10m, TestData.Jan1.AddDays(1), null, alice, bob);
        await AddAsync(userId, group.Id, alice, 10m, TestData.Jan1.AddDays(8), null, alice, bob);

        var dashboard = await Stats.GetDashboardAsync(userId,
            new StatsQuery(GroupId: group.Id, Granularity: granularity));

        dashboard.SpendOverTime.Count.ShouldBe(expected);
    }

    [Fact]
    public async Task Spending_by_member_shows_what_each_person_paid_and_owes()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        await AddAsync(userId, group.Id, alice, 100m, TestData.Jan1, null, alice, bob);
        await AddAsync(userId, group.Id, bob, 40m, TestData.Jan1, null, alice, bob);

        var dashboard = await Stats.GetDashboardAsync(userId, new StatsQuery(GroupId: group.Id));

        var aliceRow = dashboard.ByMember.First(m => m.MemberId == alice);
        aliceRow.Paid.ShouldBe(100m);
        aliceRow.Owed.ShouldBe(70m);
        aliceRow.Net.ShouldBe(30m);
        dashboard.ByMember.Sum(m => m.Net).ShouldBe(0m);
    }

    [Fact]
    public async Task Who_owes_whom_is_reported_over_time()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        await AddAsync(userId, group.Id, alice, 100m, TestData.Jan1, null, alice, bob);
        await AddAsync(userId, group.Id, bob, 60m, TestData.Jan1.AddMonths(1), null, alice, bob);

        var dashboard = await Stats.GetDashboardAsync(userId, new StatsQuery(GroupId: group.Id));

        var aliceTrend = dashboard.DebtTrends.Where(t => t.MemberId == alice).OrderBy(t => t.Bucket).ToList();
        aliceTrend.Count.ShouldBe(2);
        // The trend is cumulative: 50 up after January, 20 up after February.
        aliceTrend[0].Net.ShouldBe(50m);
        aliceTrend[1].Net.ShouldBe(20m);
    }

    [Fact]
    public async Task The_dashboard_honours_a_date_range()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        await AddAsync(userId, group.Id, alice, 100m, TestData.Jan1, null, alice, bob);
        await AddAsync(userId, group.Id, alice, 50m, TestData.Jan1.AddMonths(6), null, alice, bob);

        var dashboard = await Stats.GetDashboardAsync(userId, new StatsQuery(
            GroupId: group.Id, From: TestData.Jan1.AddMonths(3)));

        dashboard.TotalSpend.ShouldBe(50m);
    }

    [Fact]
    public async Task A_deleted_expense_is_left_out_of_the_stats()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        await AddAsync(userId, group.Id, alice, 100m, TestData.Jan1, null, alice, bob);
        var doomed = await Expenses.CreateAsync(userId, new CreateExpenseRequest(
            group.Id, alice, "Doomed", 999m, "CAD", TestData.Jan1, SplitType.Equal,
            [new SplitInputDto(alice, null)], null, null, null, null, null, null));
        await Expenses.DeleteAsync(userId, doomed.Id);

        (await Stats.GetDashboardAsync(userId, new StatsQuery(GroupId: group.Id)))
            .TotalSpend.ShouldBe(100m);
    }

    [Fact]
    public async Task A_settlement_does_not_count_as_spending()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        await AddAsync(userId, group.Id, alice, 100m, TestData.Jan1, null, alice, bob);
        await Settlements.CreateAsync(userId, new SplitEverything.Application.Contracts.Settlements.CreateSettlementRequest(
            group.Id, bob, alice, 50m, "CAD", TestData.Jan1, null, null, null));

        var dashboard = await Stats.GetDashboardAsync(userId, new StatsQuery(GroupId: group.Id));

        dashboard.TotalSpend.ShouldBe(100m);
        // The member view is a ledger though, so settling shows up there.
        dashboard.ByMember.First(m => m.MemberId == alice).Net.ShouldBe(0m);
    }

    [Fact]
    public async Task Without_a_group_the_dashboard_spans_every_group()
    {
        var (userId, first, alice, bob) = await SetupAsync();
        await AddAsync(userId, first.Id, alice, 100m, TestData.Jan1, null, alice, bob);
        var second = await Groups.CreateAsync(userId,
            new CreateGroupRequest("Trip", "CAD", null, null, null, null));
        var me = second.Members.Single().Id;
        await AddAsync(userId, second.Id, me, 40m, TestData.Jan1, null, me);

        (await Stats.GetDashboardAsync(userId, new StatsQuery())).TotalSpend.ShouldBe(140m);
    }

    [Fact]
    public async Task Archived_groups_are_left_out_unless_asked_for()
    {
        var (userId, group, alice, bob) = await SetupAsync();
        await AddAsync(userId, group.Id, alice, 100m, TestData.Jan1, null, alice, bob);
        await Groups.ArchiveAsync(userId, group.Id);

        (await Stats.GetDashboardAsync(userId, new StatsQuery())).TotalSpend.ShouldBe(0m);
        (await Stats.GetDashboardAsync(userId, new StatsQuery(IncludeArchived: true))).TotalSpend.ShouldBe(100m);
    }

    [Fact]
    public async Task A_cross_group_dashboard_reports_in_the_users_currency()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id,
            new CreateGroupRequest("Euro trip", "EUR", null, null, null, null));
        var me = group.Members.Single().Id;
        await Expenses.CreateAsync(user.Id, new CreateExpenseRequest(
            group.Id, me, "Hotel", 100m, "EUR", TestData.Jan1, SplitType.Equal,
            [new SplitInputDto(me, null)], null, null, null, null, null, null));
        Currency.GetRateAsync("EUR", "CAD", Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1.48m));

        var dashboard = await Stats.GetDashboardAsync(user.Id, new StatsQuery());

        dashboard.Currency.ShouldBe("CAD");
        dashboard.TotalSpend.ShouldBe(148m);
    }

    /// <summary>
    /// The odd cent in a bucket's category split.
    ///
    /// It only shows up once amounts have been converted, which is the one place
    /// a stored amount stops being a round number of cents. The line for a
    /// category is drawn against the bar's own height, so the parts have to come
    /// to the whole: a cent adrift is a line that misses the top of a bar that it
    /// was the entirety of. The largest absorbs it, as everywhere else here.
    /// </summary>
    [Fact]
    public async Task A_bucket_that_does_not_divide_evenly_still_adds_up()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id,
            new CreateGroupRequest("Euro trip", "EUR", null, null, null, null));
        var me = group.Members.Single().Id;

        // 6.67 at 1.5 is 10.005 - half a cent, twice, in one month.
        foreach (var category in new[] { "groceries", "dining" })
        {
            await Expenses.CreateAsync(user.Id, new CreateExpenseRequest(
                group.Id, me, "Expense", 6.67m, "EUR", TestData.Jan1, SplitType.Equal,
                [new SplitInputDto(me, null)], null, null, null, null, null, null, null, category));
        }

        Currency.GetRateAsync("EUR", "CAD", Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1.5m));

        var dashboard = await Stats.GetDashboardAsync(user.Id, new StatsQuery());

        var bucket = dashboard.SpendOverTime.ShouldHaveSingleItem();
        bucket.Amount.ShouldBe(20.01m);

        // Rounded on their own these come to 20.00, a cent short of the bar.
        bucket.ByCategory!.Sum(c => c.Amount).ShouldBe(bucket.Amount);
        bucket.ByCategory!.Select(c => c.Amount).ShouldBe([10.01m, 10.00m]);
    }

    [Fact]
    public async Task The_dashboard_of_a_group_you_are_not_in_is_forbidden()
    {
        var (_, group, _, _) = await SetupAsync();
        var stranger = await TestData.SeedUserAsync(Db, "Stranger");

        await Should.ThrowAsync<ForbiddenException>(
            () => Stats.GetDashboardAsync(stranger.Id, new StatsQuery(GroupId: group.Id)));
    }

    [Fact]
    public async Task An_unknown_granularity_is_rejected()
    {
        var (userId, group, _, _) = await SetupAsync();

        await Should.ThrowAsync<ValidationException>(() => Stats.GetDashboardAsync(
            userId, new StatsQuery(GroupId: group.Id, Granularity: "fortnight")));
    }
}
