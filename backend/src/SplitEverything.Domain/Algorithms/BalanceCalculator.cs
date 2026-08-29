using SplitEverything.Domain.Common;

namespace SplitEverything.Domain.Algorithms;

/// <summary>An expense reduced to what the balance math needs.</summary>
public sealed record BalanceExpense(
    Guid PayerMemberId,
    decimal AmountInBaseCurrency,
    IReadOnlyList<(Guid MemberId, decimal AmountInBaseCurrency)> Splits);

public sealed record BalanceSettlement(
    Guid FromMemberId,
    Guid ToMemberId,
    decimal AmountInBaseCurrency);

public sealed record PairwiseDebt(Guid FromMemberId, Guid ToMemberId, decimal Amount);

/// <summary>
/// Net positions and raw pairwise debts for a group.
///
/// Everything works in the group's base currency using the rate frozen on each
/// expense, so a later FX move never rewrites a settled balance.
/// </summary>
public static class BalanceCalculator
{
    public static IReadOnlyList<MemberBalance> NetBalances(
        IEnumerable<Guid> memberIds,
        IEnumerable<BalanceExpense> expenses,
        IEnumerable<BalanceSettlement> settlements,
        string currency = "CAD")
    {
        var net = memberIds.Distinct().ToDictionary(id => id, _ => 0m);

        void Bump(Guid memberId, decimal delta)
        {
            // Members who left still carry history, so accept ids not in the roster.
            net[memberId] = net.GetValueOrDefault(memberId) + delta;
        }

        foreach (var expense in expenses)
        {
            Bump(expense.PayerMemberId, expense.AmountInBaseCurrency);
            foreach (var (memberId, amount) in expense.Splits)
                Bump(memberId, -amount);
        }

        foreach (var settlement in settlements)
        {
            // Paying someone reduces what you owe and what they are owed.
            Bump(settlement.FromMemberId, settlement.AmountInBaseCurrency);
            Bump(settlement.ToMemberId, -settlement.AmountInBaseCurrency);
        }

        return net
            .Select(kv => new MemberBalance(kv.Key, CurrencyPrecision.Round(kv.Value, currency)))
            .OrderBy(b => b.MemberId)
            .ToList();
    }

    /// <summary>
    /// Who owes whom before simplification: the honest, unreduced view, which some
    /// people prefer because it shows the actual expense that created the debt.
    /// </summary>
    public static IReadOnlyList<PairwiseDebt> PairwiseDebts(
        IEnumerable<BalanceExpense> expenses,
        IEnumerable<BalanceSettlement> settlements,
        string currency = "CAD")
    {
        var ledger = new Dictionary<(Guid From, Guid To), decimal>();

        void Add(Guid from, Guid to, decimal amount)
        {
            if (from == to || amount == 0m) return;

            // Fold the reverse direction into a single signed entry per unordered pair.
            if (ledger.TryGetValue((to, from), out var reverse))
            {
                ledger[(to, from)] = reverse - amount;
                return;
            }
            ledger[(from, to)] = ledger.GetValueOrDefault((from, to)) + amount;
        }

        foreach (var expense in expenses)
            foreach (var (memberId, amount) in expense.Splits)
                Add(memberId, expense.PayerMemberId, amount);

        foreach (var settlement in settlements)
            Add(settlement.ToMemberId, settlement.FromMemberId, settlement.AmountInBaseCurrency);

        var epsilon = CurrencyPrecision.MinorUnit(currency) / 2m;
        var result = new List<PairwiseDebt>();

        foreach (var (pair, amount) in ledger)
        {
            var rounded = CurrencyPrecision.Round(amount, currency);
            if (Math.Abs(rounded) <= epsilon) continue;

            result.Add(rounded > 0
                ? new PairwiseDebt(pair.From, pair.To, rounded)
                : new PairwiseDebt(pair.To, pair.From, -rounded));
        }

        return result
            .OrderBy(d => d.FromMemberId)
            .ThenBy(d => d.ToMemberId)
            .ToList();
    }
}
