using SplitEverything.Domain.Common;

namespace SplitEverything.Domain.Algorithms;

public sealed record MemberBalance(Guid MemberId, decimal Net);

public sealed record DebtTransfer(Guid FromMemberId, Guid ToMemberId, decimal Amount);

public static class DebtSimplifier
{
    public static IReadOnlyList<DebtTransfer> Simplify(
        IReadOnlyList<MemberBalance> balances, string currency = "CAD")
    {
        var unit = CurrencyPrecision.MinorUnit(currency);
        var epsilon = unit / 2m;

        var creditors = new List<MemberBalance>();
        var debtors = new List<MemberBalance>();

        foreach (var balance in balances)
        {
            var net = CurrencyPrecision.Round(balance.Net, currency);
            if (net > epsilon) creditors.Add(new MemberBalance(balance.MemberId, net));
            else if (net < -epsilon) debtors.Add(new MemberBalance(balance.MemberId, -net));
        }

        if (creditors.Count == 0 || debtors.Count == 0)
            return Array.Empty<DebtTransfer>();

        creditors.Sort(Compare);
        debtors.Sort(Compare);

        var transfers = new List<DebtTransfer>();
        var ci = 0;
        var di = 0;
        var creditRemaining = creditors[0].Net;
        var debtRemaining = debtors[0].Net;

        while (ci < creditors.Count && di < debtors.Count)
        {
            var amount = Math.Min(creditRemaining, debtRemaining);
            if (amount > epsilon)
            {
                transfers.Add(new DebtTransfer(
                    debtors[di].MemberId,
                    creditors[ci].MemberId,
                    CurrencyPrecision.Round(amount, currency)));
            }

            creditRemaining -= amount;
            debtRemaining -= amount;

            if (creditRemaining <= epsilon && ++ci < creditors.Count)
                creditRemaining = creditors[ci].Net;
            if (debtRemaining <= epsilon && ++di < debtors.Count)
                debtRemaining = debtors[di].Net;
        }

        return transfers;

        static int Compare(MemberBalance a, MemberBalance b)
        {
            var byAmount = b.Net.CompareTo(a.Net);
            return byAmount != 0 ? byAmount : a.MemberId.CompareTo(b.MemberId);
        }
    }
}
