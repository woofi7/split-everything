using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using SplitEverything.Application.Contracts.Expenses;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Application.Contracts.Stats;
using SplitEverything.Application.Contracts.Sync;
using SplitEverything.Domain.Common;
using SplitEverything.Infrastructure.Services;
using SplitEverything.Tests.Support;

namespace SplitEverything.Tests.Application;

public class ExpenseCategoryTests(PostgresFixture fixture) : ServiceTestBase(fixture)
{
    private async Task<(Guid UserId, GroupDto Group, Guid PayerId)> SetupAsync()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, ["Bob"]));
        var payer = group.Members.First(m => m.UserId == user.Id).Id;

        return (user.Id, group, payer);
    }

    private CreateExpenseRequest Expense(
        GroupDto group, Guid payer, string description, decimal amount, string? category)
        => new(group.Id, payer, description, amount, "CAD", Clock.UtcNow, SplitType.Equal,
            group.Members.Select(m => new SplitInputDto(m.Id, null)).ToList(),
            null, null, null, null, null, null, null, category);

    [Fact]
    public async Task An_expense_is_filed_under_nothing_unless_somebody_says()
    {
        var (userId, group, payer) = await SetupAsync();

        var expense = await Expenses.CreateAsync(userId, Expense(group, payer, "Dinner", 40m, null));

        expense.CategoryKey.ShouldBeNull();
    }

    [Fact]
    public async Task It_keeps_what_it_was_filed_under()
    {
        var (userId, group, payer) = await SetupAsync();

        var expense = await Expenses.CreateAsync(
            userId, Expense(group, payer, "Metro", 62m, "groceries"));

        expense.CategoryKey.ShouldBe("groceries");
        (await Db.Expenses.FirstAsync(e => e.Id == expense.Id)).CategoryKey.ShouldBe("groceries");
    }

    [Fact]
    public async Task A_key_is_stored_as_it_will_be_matched()
    {
        var (userId, group, payer) = await SetupAsync();

        var expense = await Expenses.CreateAsync(
            userId, Expense(group, payer, "Metro", 62m, "  Groceries "));

        expense.CategoryKey.ShouldBe("groceries");
    }

    [Fact]
    public async Task A_key_no_category_answers_to_is_kept_rather_than_refused()
    {
        var (userId, group, payer) = await SetupAsync();

        var expense = await Expenses.CreateAsync(
            userId, Expense(group, payer, "Ski pass", 80m, "ski"));

        expense.CategoryKey.ShouldBe("ski");
    }

    [Fact]
    public async Task Editing_refiles_it_and_an_empty_string_unfiles_it()
    {
        var (userId, group, payer) = await SetupAsync();
        var expense = await Expenses.CreateAsync(
            userId, Expense(group, payer, "Metro", 62m, "groceries"));

        var refiled = await Expenses.UpdateAsync(userId, expense.Id,
            new UpdateExpenseRequest(null, null, null, null, null, null, null, null, null, null,
                null, null, "dining"));
        refiled.CategoryKey.ShouldBe("dining");

        var unfiled = await Expenses.UpdateAsync(userId, expense.Id,
            new UpdateExpenseRequest(null, null, null, null, null, null, null, null, null, null,
                null, null, ""));
        unfiled.CategoryKey.ShouldBeNull();
    }

    [Fact]
    public async Task Not_saying_anything_leaves_the_filing_alone()
    {
        var (userId, group, payer) = await SetupAsync();
        var expense = await Expenses.CreateAsync(
            userId, Expense(group, payer, "Metro", 62m, "groceries"));

        var renamed = await Expenses.UpdateAsync(userId, expense.Id,
            new UpdateExpenseRequest(null, "Metro plus", null, null, null, null, null, null,
                null, null, null));

        renamed.CategoryKey.ShouldBe("groceries");
    }

    [Fact]
    public async Task It_rides_along_in_the_payload_other_devices_read()
    {
        var (userId, group, payer) = await SetupAsync();
        var expense = await Expenses.CreateAsync(
            userId, Expense(group, payer, "Metro", 62m, "groceries"));

        var entry = await Db.SyncLog
            .Where(l => l.EntityId == expense.Id)
            .OrderByDescending(l => l.ServerSeq)
            .FirstAsync();

        entry.PayloadJson.ShouldContain("groceries");
    }

    [Fact]
    public async Task The_stats_screen_totals_by_category()
    {
        var (userId, group, payer) = await SetupAsync();

        await Expenses.CreateAsync(userId, Expense(group, payer, "Metro", 60m, "groceries"));
        await Expenses.CreateAsync(userId, Expense(group, payer, "IGA", 40m, "groceries"));
        await Expenses.CreateAsync(userId, Expense(group, payer, "Resto", 25m, "dining"));
        await Expenses.CreateAsync(userId, Expense(group, payer, "Something", 10m, null));

        var stats = new StatsService(Db, Currency);
        var dashboard = await stats.GetDashboardAsync(userId, new StatsQuery(GroupId: group.Id));

        var byCategory = dashboard.ByCategory!;
        byCategory[0].ShouldBe(new CategorySpendDto("groceries", 100m, 2));
        byCategory[1].ShouldBe(new CategorySpendDto("dining", 25m, 1));

        byCategory.ShouldContain(new CategorySpendDto(null, 10m, 1));
    }

    [Fact]
    public async Task Filing_many_at_once_arrives_as_ordinary_updates()
    {
        var (userId, group, payer) = await SetupAsync();

        var metro = await Expenses.CreateAsync(userId, Expense(group, payer, "Metro", 60m, null));
        var iga = await Expenses.CreateAsync(userId, Expense(group, payer, "IGA", 40m, null));

        var sync = new SyncService(Db, Writer, Broadcaster, Clock, Activity);

        var result = await sync.PushAsync(userId, new SyncPushRequest(TestData.DeviceB, [
            Operation(group.Id, SyncEntityType.Expense, metro.Id, SyncOperation.Update,
                RefiledJson(metro, "groceries"),
                new Dictionary<string, long>(metro.VectorClock) { [TestData.DeviceB] = 1 }),
            Operation(group.Id, SyncEntityType.Expense, iga.Id, SyncOperation.Update,
                RefiledJson(iga, "groceries"),
                new Dictionary<string, long>(iga.VectorClock) { [TestData.DeviceB] = 1 }),
        ]));

        result.Rejected.ShouldBeEmpty();
        result.Conflicts.ShouldBeEmpty();

        var stored = await NewContext().Expenses
            .Include(e => e.Splits)
            .Where(e => e.GroupId == group.Id)
            .OrderBy(e => e.Amount)
            .ToListAsync();

        stored.Select(e => e.CategoryKey).ShouldAllBe(key => key == "groceries");

        stored[0].Amount.ShouldBe(40m);
        stored[1].Amount.ShouldBe(60m);
        stored.Sum(e => e.Splits.Count).ShouldBe(4);
    }

    private static string RefiledJson(ExpenseDto expense, string categoryKey)
        => JsonSerializer.Serialize(new
        {
            id = expense.Id,
            groupId = expense.GroupId,
            paidByMemberId = expense.PaidByMemberId,
            description = expense.Description,
            amount = expense.Amount,
            currency = expense.Currency,
            amountInBaseCurrency = expense.AmountInBaseCurrency,
            exchangeRate = expense.ExchangeRate,
            spentAt = expense.SpentAt,
            splitType = (int)expense.SplitType,
            categoryKey,
            splits = expense.Splits.Select(s => new
            {
                memberId = s.MemberId,
                amount = s.Amount,
                amountInBaseCurrency = s.AmountInBaseCurrency,
            }),
        });
}
