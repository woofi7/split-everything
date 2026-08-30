using SplitEverything.Domain.Common;

namespace SplitEverything.Domain.Algorithms;

/// <summary>
/// An expense reduced to what the balance math needs: who put money in, and who
/// owes for it.
///
/// Payers is a list because an expense can be paid by more than one person at once,
/// and no single-payer stand-in gets the balances right: two people paying 40 and 25
/// of a 65 bill split evenly are 7.50 apart, not 32.50.
/// </summary>
public sealed record BalanceExpense(
    IReadOnlyList<(Guid MemberId, decimal AmountInBaseCurrency)> Payers,
    IReadOnlyList<(Guid MemberId, decimal AmountInBaseCurrency)> Splits)
{
    /// <summary>The ordinary case, where one person paid for the lot.</summary>
    public static BalanceExpense PaidBy(
        Guid payerMemberId,
        decimal amountInBaseCurrency,
        IReadOnlyList<(Guid MemberId, decimal AmountInBaseCurrency)> splits)
        => new([(payerMemberId, amountInBaseCurrency)], splits);
}

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
            foreach (var (memberId, amount) in expense.Payers)
                Bump(memberId, amount);

            foreach (var (memberId, amount) in expense.Splits)
                Bump(memberId, -amount);
        }

        foreach (var settlement in settlements)
        {
            // Paying someone reduces what you owe and what they are owed.
            Bump(settlement.FromMemberId, settlement.AmountInBaseCurrency);
            Bump(settlement.ToMemberId, -settlement.AmountInBaseCurrency);
        }

        var rounded = net
            .Select(kv => new MemberBalance(kv.Key, CurrencyPrecision.Round(kv.Value, currency)))
            .OrderBy(b => b.MemberId)
            .ToList();

        /*
         * Balances have to sum to zero, and rounding each of them to a payable cent
         * can leave them a cent short of it: shares are worked out finer than the
         * currency, so a net position can be a fraction of a cent either way.
         *
         * The residue goes to the largest balance, as everywhere else in this app.
         * Left in, it is a cent nobody can pay off - the settle-up plan can only move
         * whole cents, so it would hand somebody a debt of one that survives paying it.
         */
        var residue = CurrencyPrecision.Round(-rounded.Sum(b => b.Net), currency);
        if (residue == 0m || rounded.Count == 0) return rounded;

        var index = Enumerable.Range(0, rounded.Count)
            .OrderByDescending(i => Math.Abs(rounded[i].Net))
            .ThenBy(i => rounded[i].MemberId)
            .First();

        rounded[index] = rounded[index] with
        {
            Net = CurrencyPrecision.Round(rounded[index].Net + residue, currency)
        };

        return rounded;
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
        {
            var paid = expense.Payers.Sum(p => p.AmountInBaseCurrency);
            if (paid == 0m) continue;

            foreach (var (memberId, owed) in expense.Splits)
            {
                // Owed to whoever put the money in, in the proportion each of them
                // did: of a bill two people covered 40/25, a share is owed 40/65 to
                // one and 25/65 to the other. Rounded once at the end, below, so the
                // proportions do not each lose a cent on the way.
                foreach (var payer in expense.Payers)
                    Add(memberId, payer.MemberId, owed * payer.AmountInBaseCurrency / paid);
            }
        }

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
