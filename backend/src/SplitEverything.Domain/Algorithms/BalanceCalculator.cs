using SplitEverything.Domain.Common;

namespace SplitEverything.Domain.Algorithms;

public sealed record BalanceExpense(
    IReadOnlyList<(Guid MemberId, decimal AmountInBaseCurrency)> Payers,
    IReadOnlyList<(Guid MemberId, decimal AmountInBaseCurrency)> Splits)
{
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
            Bump(settlement.FromMemberId, settlement.AmountInBaseCurrency);
            Bump(settlement.ToMemberId, -settlement.AmountInBaseCurrency);
        }

        var rounded = net
            .Select(kv => new MemberBalance(kv.Key, CurrencyPrecision.Round(kv.Value, currency)))
            .OrderBy(b => b.MemberId)
            .ToList();

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

    public static IReadOnlyList<PairwiseDebt> PairwiseDebts(
        IEnumerable<BalanceExpense> expenses,
        IEnumerable<BalanceSettlement> settlements,
        string currency = "CAD")
    {
        var ledger = new Dictionary<(Guid From, Guid To), decimal>();

        void Add(Guid from, Guid to, decimal amount)
        {
            if (from == to || amount == 0m) return;

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
