using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Expenses;
using SplitEverything.Application.Services;
using SplitEverything.Domain.Algorithms;
using SplitEverything.Domain.Common;
using SplitEverything.Domain.Entities;
using SplitEverything.Infrastructure.Persistence;
using SplitEverything.Infrastructure.Sync;

namespace SplitEverything.Infrastructure.Services;

/// <summary>
/// Rent, subscriptions and anything else on a schedule.
///
/// A rule is a template plus a next-run cursor; a worker materialises real
/// expenses from it. Occurrences are backfilled rather than skipped when the app
/// was down, and the cursor advances only for occurrences actually written, so a
/// crash mid-run cannot lose a month.
/// </summary>
public sealed class RecurringExpenseService(
    AppDbContext db,
    ISyncWriter writer,
    IActivityService activity,
    ICurrencyConverter currency,
    IPushDispatcher push,
    IClock clock) : IRecurringExpenseService
{
    public async Task<RecurringExpenseDto> CreateAsync(
        Guid userId, CreateRecurringExpenseRequest request, CancellationToken ct = default)
    {
        await GroupAccess.RequireMemberAsync(db, userId, request.GroupId, ct);
        var group = await GroupAccess.RequireGroupAsync(db, request.GroupId, ct);
        GroupAccess.RequireWritable(group);

        var description = GroupAccess.RequireText(request.Description, "Description", 500);
        var ruleCurrency = GroupAccess.NormalizeCurrency(request.Currency);

        if (request.Amount <= 0m)
            throw new ValidationException("A recurring expense needs an amount greater than zero.");
        if (request.Interval < 1)
            throw new ValidationException("The interval must be at least 1.");
        if (request.Splits.Count == 0)
            throw new ValidationException("A recurring expense needs at least one participant.");

        var members = (await db.GroupMembers
            .Where(m => m.GroupId == request.GroupId && !m.IsDeleted)
            .Select(m => m.Id).ToListAsync(ct)).ToHashSet();

        if (!members.Contains(request.PaidByMemberId))
            throw new ValidationException("The payer must be a member of this group.");
        if (request.Splits.Any(s => !members.Contains(s.MemberId)))
            throw new ValidationException("Every participant must be a member of this group.");

        var rule = new RecurringExpense
        {
            GroupId = request.GroupId,
            PaidByMemberId = request.PaidByMemberId,
            Description = description,
            Amount = request.Amount,
            Currency = ruleCurrency,
            CategoryId = request.CategoryId,
            SplitType = request.SplitType,
            SplitTemplateJson = JsonSerializer.Serialize(request.Splits),
            Unit = request.Unit,
            Interval = request.Interval,
            DayOfMonth = request.DayOfMonth,
            DayOfWeek = request.DayOfWeek,
            StartsOn = request.StartsOn,
            EndsOn = request.EndsOn,
            MaxOccurrences = request.MaxOccurrences,
            // The first run is the start date itself, so a rule starting today fires today.
            NextRunAt = request.StartsOn,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow
        };
        db.RecurringExpenses.Add(rule);

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        return Map(rule);
    }

    public async Task<IReadOnlyList<RecurringExpenseDto>> ListAsync(
        Guid userId, Guid groupId, CancellationToken ct = default)
    {
        await GroupAccess.RequireMemberAsync(db, userId, groupId, ct);

        return await db.RecurringExpenses
            .Where(r => r.GroupId == groupId && !r.IsDeleted)
            .OrderBy(r => r.NextRunAt)
            .Select(r => new RecurringExpenseDto(
                r.Id, r.GroupId, r.Description, r.Amount, r.Currency,
                r.Unit, r.Interval, r.NextRunAt, r.LastRunAt,
                r.OccurrenceCount, r.MaxOccurrences, r.IsPaused))
            .ToListAsync(ct);
    }

    public async Task<RecurringExpenseDto> SetPausedAsync(
        Guid userId, Guid id, bool paused, CancellationToken ct = default)
    {
        var rule = await LoadAsync(userId, id, ct);
        rule.IsPaused = paused;
        rule.UpdatedAt = clock.UtcNow;

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        return Map(rule);
    }

    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var rule = await LoadAsync(userId, id, ct);

        // Tombstoned, not removed: the expenses it already generated point at it and
        // deleting the rule must not rewrite history.
        rule.IsDeleted = true;
        rule.DeletedAt = clock.UtcNow;

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    public async Task<int> RunDueAsync(DateTimeOffset asOf, CancellationToken ct = default)
    {
        var due = await db.RecurringExpenses
            .Where(r => !r.IsDeleted && !r.IsPaused && r.NextRunAt <= asOf)
            .OrderBy(r => r.NextRunAt)
            .ToListAsync(ct);

        var created = 0;

        foreach (var rule in due)
        {
            var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == rule.GroupId, ct);
            // An archived group is frozen; the rule stays but stops producing.
            if (group is null || group.IsArchived) continue;

            var until = rule.EndsOn is { } endsOn && endsOn < asOf ? endsOn : asOf;

            var occurrences = RecurrenceSchedule.Occurrences(
                rule.StartsOn,
                // Strictly after the last one written, so a re-run is a no-op.
                rule.LastRunAt ?? rule.StartsOn.AddTicks(-1),
                until,
                rule.Unit, rule.Interval, rule.DayOfMonth, rule.DayOfWeek);

            if (rule.MaxOccurrences is { } max)
            {
                var remaining = max - rule.OccurrenceCount;
                if (remaining <= 0) continue;
                occurrences = occurrences.Take(remaining).ToList();
            }

            foreach (var occurrence in occurrences)
            {
                await MaterialiseAsync(rule, group, occurrence, ct);
                rule.OccurrenceCount += 1;
                rule.LastRunAt = occurrence;
                created++;
            }

            rule.NextRunAt = RecurrenceSchedule.Next(
                rule.LastRunAt ?? rule.StartsOn, rule.Unit, rule.Interval, rule.DayOfMonth, rule.DayOfWeek);

            if (rule.MaxOccurrences is { } limit && rule.OccurrenceCount >= limit)
                rule.IsPaused = true;
            if (rule.EndsOn is { } ends && rule.NextRunAt > ends)
                rule.IsPaused = true;

            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }

        return created;
    }

    private async Task MaterialiseAsync(
        RecurringExpense rule, Group group, DateTimeOffset occurredAt, CancellationToken ct)
    {
        var template = JsonSerializer.Deserialize<List<SplitInputDto>>(rule.SplitTemplateJson) ?? [];
        if (template.Count == 0) return;

        var conversion = string.Equals(rule.Currency, group.BaseCurrency, StringComparison.OrdinalIgnoreCase)
            ? new ConversionResult(rule.Amount, 1m, occurredAt)
            : await currency.ConvertAsync(rule.Amount, rule.Currency, group.BaseCurrency, occurredAt, ct);

        var shares = SplitCalculator.Calculate(rule.Amount, rule.Currency, rule.SplitType,
            template.Select(s => new SplitInput(s.MemberId, s.Value)).ToList());

        var expense = new Expense
        {
            GroupId = rule.GroupId,
            PaidByMemberId = rule.PaidByMemberId,
            Description = rule.Description,
            Amount = rule.Amount,
            Currency = rule.Currency,
            AmountInBaseCurrency = conversion.Amount,
            ExchangeRate = conversion.Rate,
            ExchangeRateAsOf = conversion.RateAsOf,
            SpentAt = occurredAt,
            CategoryId = rule.CategoryId,
            SplitType = rule.SplitType,
            RecurringExpenseId = rule.Id,
            OriginLineageId = group.LineageId,
            Revision = 1,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow
        };
        db.Expenses.Add(expense);

        foreach (var share in shares)
        {
            db.ExpenseSplits.Add(new ExpenseSplit
            {
                ExpenseId = expense.Id,
                GroupId = rule.GroupId,
                MemberId = share.MemberId,
                Amount = share.Amount,
                AmountInBaseCurrency = CurrencyPrecision.Round(
                    share.Amount * conversion.Rate, group.BaseCurrency),
                InputValue = share.InputValue,
                CreatedAt = clock.UtcNow,
                UpdatedAt = clock.UtcNow
            });
        }

        // No user is acting, so the rule itself is the writing device: occurrences
        // stay causally ordered without borrowing someone's identity.
        var deviceId = $"recurring:{rule.Id:N}";

        await writer.RecordAsync(expense, SyncEntityType.Expense, rule.GroupId,
            SyncOperation.Create, deviceId, null, ExpenseService.ExpensePayload(expense), ct: ct);

        await activity.RecordAsync(rule.GroupId, ActivityKind.ExpenseCreated, null, rule.PaidByMemberId,
            SyncEntityType.Expense, expense.Id,
            $"{rule.Description} was added automatically", new { recurring = true }, ct);

        await db.SaveChangesAsync(ct);

        await push.SendToGroupAsync(rule.GroupId, new PushMessage(
            group.Name,
            $"{rule.Description} was added automatically",
            $"/groups/{rule.GroupId}/expenses/{expense.Id}"), null, ct);
    }

    private async Task<RecurringExpense> LoadAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var rule = await db.RecurringExpenses.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted, ct)
                   ?? throw new NotFoundException($"Recurring expense {id}");

        await GroupAccess.RequireMemberAsync(db, userId, rule.GroupId, ct);
        return rule;
    }

    private static RecurringExpenseDto Map(RecurringExpense rule)
        => new(rule.Id, rule.GroupId, rule.Description, rule.Amount, rule.Currency,
            rule.Unit, rule.Interval, rule.NextRunAt, rule.LastRunAt,
            rule.OccurrenceCount, rule.MaxOccurrences, rule.IsPaused);
}
