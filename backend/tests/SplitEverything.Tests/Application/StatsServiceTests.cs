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
            null, null, null, null, null, null));

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
