using Microsoft.EntityFrameworkCore;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Stats;
using SplitEverything.Application.Services;
using SplitEverything.Domain.Algorithms;
using SplitEverything.Domain.Common;
using SplitEverything.Infrastructure.Persistence;

namespace SplitEverything.Infrastructure.Services;

/// <summary>
/// Dashboard aggregates: spend over time, spend by category, who paid what, and
/// how the debts moved.
///
/// A single-group view reports in that group's base currency. A cross-group view
/// converts into the user's own currency, because adding a CAD group to a EUR group
/// raw would be meaningless.
/// </summary>
public sealed class StatsService(
    AppDbContext db,
    ICurrencyConverter currency,
    IClock clock) : IStatsService
{
    private static readonly HashSet<string> Granularities = new(StringComparer.OrdinalIgnoreCase)
    {
        "day", "week", "month"
    };

    public async Task<StatsDashboardDto> GetDashboardAsync(
        Guid userId, StatsQuery query, CancellationToken ct = default)
    {
        if (!Granularities.Contains(query.Granularity))
            throw new ValidationException("Granularity must be day, week or month.");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new NotFoundException($"User {userId}");

        var scope = await ResolveScopeAsync(userId, query, ct);

        if (scope.GroupIds.Count == 0)
        {
            return new StatsDashboardDto(scope.Currency, 0m, 0m, 0m, 0,
                query.From, query.To, [], [], [], []);
        }

        var expenses = await db.Expenses
            .Where(e => scope.GroupIds.Contains(e.GroupId) && !e.IsDeleted)
            .Where(e => query.From == null || e.SpentAt >= query.From)
            .Where(e => query.To == null || e.SpentAt <= query.To)
            .Select(e => new
            {
                e.Id,
                e.GroupId,
                e.SpentAt,
                e.AmountInBaseCurrency,
                e.CategoryId,
                CategoryKey = e.Category == null ? null : e.Category.Key,
                CategoryName = e.Category == null ? null : e.Category.Name,
                CategoryIcon = e.Category == null ? null : e.Category.IconName,
                CategoryColor = e.Category == null ? null : e.Category.ColorHex,
                e.PaidByMemberId,
                Splits = e.Splits.Where(s => !s.IsDeleted)
                    .Select(s => new { s.MemberId, s.AmountInBaseCurrency }).ToList()
            })
            .ToListAsync(ct);

        var settlements = await db.Settlements
            .Where(s => scope.GroupIds.Contains(s.GroupId) && !s.IsDeleted)
            .Where(s => query.From == null || s.SettledAt >= query.From)
            .Where(s => query.To == null || s.SettledAt <= query.To)
            .Select(s => new { s.GroupId, s.SettledAt, s.FromMemberId, s.ToMemberId, s.AmountInBaseCurrency })
            .ToListAsync(ct);

        var members = await db.GroupMembers
            .Where(m => scope.GroupIds.Contains(m.GroupId) && !m.IsDeleted)
            .Select(m => new { m.Id, m.GroupId, m.DisplayName, m.UserId })
            .ToListAsync(ct);

        var myMemberIds = members.Where(m => m.UserId == userId).Select(m => m.Id).ToHashSet();
        var names = members.ToDictionary(m => m.Id, m => m.DisplayName);

        // Each group's amounts are in its own base currency, so everything is
        // normalised once, up front, before any aggregate is computed.
        var factors = await BuildFactorsAsync(scope, ct);
        decimal Normalise(Guid groupId, decimal amount) => amount * factors[groupId];

        var totalSpend = expenses.Sum(e => Normalise(e.GroupId, e.AmountInBaseCurrency));
        var myShare = expenses.Sum(e => Normalise(e.GroupId,
            e.Splits.Where(s => myMemberIds.Contains(s.MemberId)).Sum(s => s.AmountInBaseCurrency)));
        var myPaid = expenses
            .Where(e => myMemberIds.Contains(e.PaidByMemberId))
            .Sum(e => Normalise(e.GroupId, e.AmountInBaseCurrency));

        var spendOverTime = expenses
            .GroupBy(e => Bucket(e.SpentAt, query.Granularity))
            .OrderBy(g => g.Key)
            .Select(g => new SpendPointDto(
                g.Key,
                CurrencyPrecision.Round(g.Sum(e => Normalise(e.GroupId, e.AmountInBaseCurrency)), scope.Currency),
                g.Count()))
            .ToList();

        var byCategory = expenses
            .GroupBy(e => new { e.CategoryId, e.CategoryKey, e.CategoryName, e.CategoryIcon, e.CategoryColor })
            .Select(g =>
            {
                var amount = CurrencyPrecision.Round(
                    g.Sum(e => Normalise(e.GroupId, e.AmountInBaseCurrency)), scope.Currency);
                return new CategorySpendDto(
                    g.Key.CategoryId,
                    g.Key.CategoryKey ?? "uncategorised",
                    g.Key.CategoryName ?? "Uncategorised",
                    g.Key.CategoryIcon ?? "dot",
                    g.Key.CategoryColor ?? "#94a3b8",
                    amount,
                    g.Count(),
                    totalSpend == 0m ? 0m : Math.Round(amount / totalSpend, 4));
            })
            .OrderByDescending(c => c.Amount)
            .ToList();

        var byMember = members
            .Select(member =>
            {
                var paid = expenses
                    .Where(e => e.PaidByMemberId == member.Id)
                    .Sum(e => Normalise(e.GroupId, e.AmountInBaseCurrency));
                var owed = expenses
                    .Sum(e => Normalise(e.GroupId,
                        e.Splits.Where(s => s.MemberId == member.Id).Sum(s => s.AmountInBaseCurrency)));
                var settledOut = settlements
                    .Where(s => s.FromMemberId == member.Id)
                    .Sum(s => Normalise(s.GroupId, s.AmountInBaseCurrency));
                var settledIn = settlements
                    .Where(s => s.ToMemberId == member.Id)
                    .Sum(s => Normalise(s.GroupId, s.AmountInBaseCurrency));

                return new MemberSpendDto(
                    member.Id, member.DisplayName,
                    CurrencyPrecision.Round(paid, scope.Currency),
                    CurrencyPrecision.Round(owed, scope.Currency),
                    CurrencyPrecision.Round(paid - owed + settledOut - settledIn, scope.Currency));
            })
            .Where(m => m.Paid != 0m || m.Owed != 0m || m.Net != 0m)
            .OrderByDescending(m => m.Paid)
            .ToList();

        var debtTrends = BuildDebtTrends(
            expenses.Select(e => (e.GroupId, e.SpentAt, e.PaidByMemberId, e.AmountInBaseCurrency,
                Splits: e.Splits.Select(s => (s.MemberId, s.AmountInBaseCurrency)).ToList())).ToList(),
            settlements.Select(s => (s.GroupId, s.SettledAt, s.FromMemberId, s.ToMemberId, s.AmountInBaseCurrency)).ToList(),
            names, query.Granularity, scope.Currency, Normalise);

        return new StatsDashboardDto(
            scope.Currency,
            CurrencyPrecision.Round(totalSpend, scope.Currency),
            CurrencyPrecision.Round(myShare, scope.Currency),
            CurrencyPrecision.Round(myPaid, scope.Currency),
            expenses.Count,
            query.From, query.To,
            spendOverTime, byCategory, byMember, debtTrends);
    }

    private sealed record Scope(List<Guid> GroupIds, Dictionary<Guid, string> GroupCurrencies, string Currency);

    private async Task<Scope> ResolveScopeAsync(Guid userId, StatsQuery query, CancellationToken ct)
    {
        if (query.GroupId is { } groupId)
        {
            await GroupAccess.RequireMemberAsync(db, userId, groupId, ct);
            var group = await GroupAccess.RequireGroupAsync(db, groupId, ct);
            return new Scope([groupId],
                new Dictionary<Guid, string> { [groupId] = group.BaseCurrency },
                group.BaseCurrency);
        }

        var user = await db.Users.FirstAsync(u => u.Id == userId, ct);

        var groups = await db.GroupMembers
            .Where(m => m.UserId == userId && m.Status == MembershipStatus.Active && !m.IsDeleted)
            .Where(m => query.IncludeArchived || !m.Group!.IsArchived)
            .Select(m => new { m.GroupId, m.Group!.BaseCurrency })
            .Distinct()
            .ToListAsync(ct);

        return new Scope(
            groups.Select(g => g.GroupId).ToList(),
            groups.ToDictionary(g => g.GroupId, g => g.BaseCurrency),
            user.DefaultCurrency);
    }

    private async Task<Dictionary<Guid, decimal>> BuildFactorsAsync(Scope scope, CancellationToken ct)
    {
        var factors = new Dictionary<Guid, decimal>();

        foreach (var (groupId, groupCurrency) in scope.GroupCurrencies)
        {
            factors[groupId] = string.Equals(groupCurrency, scope.Currency, StringComparison.OrdinalIgnoreCase)
                ? 1m
                : await currency.GetRateAsync(groupCurrency, scope.Currency, null, ct);
        }

        return factors;
    }

    /// <summary>
    /// Cumulative net position per member at the end of each bucket, which is what
    /// makes the trend readable: it answers "how much was I up at that point",
    /// not "how much moved that week".
    /// </summary>
    private static List<DebtTrendPointDto> BuildDebtTrends(
        List<(Guid GroupId, DateTimeOffset SpentAt, Guid PayerId, decimal Amount,
            List<(Guid MemberId, decimal Amount)> Splits)> expenses,
        List<(Guid GroupId, DateTimeOffset SettledAt, Guid FromMemberId, Guid ToMemberId, decimal Amount)> settlements,
        Dictionary<Guid, string> names,
        string granularity,
        string reportingCurrency,
        Func<Guid, decimal, decimal> normalise)
    {
        var buckets = expenses.Select(e => Bucket(e.SpentAt, granularity))
            .Concat(settlements.Select(s => Bucket(s.SettledAt, granularity)))
            .Distinct()
            .OrderBy(b => b)
            .ToList();

        var running = new Dictionary<Guid, decimal>();
        var points = new List<DebtTrendPointDto>();

        foreach (var bucket in buckets)
        {
            foreach (var expense in expenses.Where(e => Bucket(e.SpentAt, granularity) == bucket))
            {
                Bump(expense.PayerId, normalise(expense.GroupId, expense.Amount));
                foreach (var (memberId, amount) in expense.Splits)
                    Bump(memberId, -normalise(expense.GroupId, amount));
            }

            foreach (var settlement in settlements.Where(s => Bucket(s.SettledAt, granularity) == bucket))
            {
                Bump(settlement.FromMemberId, normalise(settlement.GroupId, settlement.Amount));
                Bump(settlement.ToMemberId, -normalise(settlement.GroupId, settlement.Amount));
            }

            foreach (var (memberId, net) in running.OrderBy(kv => names.GetValueOrDefault(kv.Key, string.Empty)))
            {
                points.Add(new DebtTrendPointDto(bucket, memberId,
                    names.GetValueOrDefault(memberId, "Unknown"),
                    CurrencyPrecision.Round(net, reportingCurrency)));
            }
        }

        return points;

        void Bump(Guid memberId, decimal delta)
            => running[memberId] = running.GetValueOrDefault(memberId) + delta;
    }

    private static DateOnly Bucket(DateTimeOffset value, string granularity)
    {
        var date = DateOnly.FromDateTime(value.UtcDateTime);

        return granularity.ToLowerInvariant() switch
        {
            "day" => date,
            // Weeks start Monday, which is what a bill-splitting week looks like.
            "week" => date.AddDays(-(((int)date.DayOfWeek + 6) % 7)),
            _ => new DateOnly(date.Year, date.Month, 1)
        };
    }
}
