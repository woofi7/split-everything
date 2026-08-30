using SplitEverything.Domain.Common;

namespace SplitEverything.Domain.Algorithms;

public sealed record SplitInput(Guid MemberId, decimal? Value);

public sealed record SplitShare(Guid MemberId, decimal Amount, decimal? InputValue);

public sealed record ItemizedLine(decimal Amount, int Quantity, IReadOnlyList<Guid> MemberIds);

/// <summary>
/// Turns a total plus per-member inputs into exact amounts that sum to the total.
///
/// Every mode routes through the same largest-remainder distribution, so the sum
/// of the shares always equals the expense total to the currency's last minor
/// unit - no drifting cent that later shows up as a phantom debt.
/// </summary>
public static class SplitCalculator
{
    public static IReadOnlyList<SplitShare> Calculate(
        decimal total,
        string currency,
        SplitType splitType,
        IReadOnlyList<SplitInput> inputs)
    {
        if (inputs.Count == 0)
            throw new ArgumentException("An expense needs at least one participant.", nameof(inputs));

        var duplicate = inputs.GroupBy(i => i.MemberId).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Member {duplicate.Key} appears more than once in the split.", nameof(inputs));

        return splitType switch
        {
            SplitType.Equal => ByWeight(total, currency, inputs.Select(i => new SplitInput(i.MemberId, 1m)).ToList(), keepInput: false),
            SplitType.Percentage => Percentage(total, currency, inputs),
            SplitType.Shares => ByWeight(total, currency, inputs, keepInput: true),
            SplitType.ExactAmount => Exact(total, currency, inputs),
            SplitType.Itemized => throw new InvalidOperationException(
                "Itemized splits are computed from expense items; call CalculateItemized."),
            _ => throw new ArgumentOutOfRangeException(nameof(splitType), splitType, "Unknown split type.")
        };
    }

    /// <summary>
    /// Itemized: each line is shared equally by whoever is on it, then anything not
    /// covered by the lines (tax, tip, service) is spread over everyone who had at
    /// least one item, proportionally to what they already owe.
    /// </summary>
    public static IReadOnlyList<SplitShare> CalculateItemized(
        decimal total,
        string currency,
        IReadOnlyList<ItemizedLine> lines,
        IReadOnlyList<Guid> fallbackMemberIds)
    {
        if (lines.Count == 0)
        {
            if (fallbackMemberIds.Count == 0)
                throw new ArgumentException("An itemized expense needs items or participants.", nameof(lines));
            return Calculate(total, currency, SplitType.Equal,
                fallbackMemberIds.Select(id => new SplitInput(id, null)).ToList());
        }

        var raw = new Dictionary<Guid, decimal>();
        var itemisedTotal = 0m;

        foreach (var line in lines)
        {
            var participants = line.MemberIds.Count > 0 ? line.MemberIds : fallbackMemberIds;
            if (participants.Count == 0)
                throw new ArgumentException("An item has no participants and no group fallback.", nameof(lines));

            var lineTotal = line.Amount * Math.Max(1, line.Quantity);
            itemisedTotal += lineTotal;

            // Split the line itself with the same exact-sum guarantee, so per-line
            // rounding cannot accumulate across a long receipt.
            var lineShares = ByWeight(lineTotal, currency,
                participants.Select(id => new SplitInput(id, 1m)).ToList(), keepInput: false);

            foreach (var share in lineShares)
                raw[share.MemberId] = raw.GetValueOrDefault(share.MemberId) + share.Amount;
        }

        var remainder = total - itemisedTotal;
        if (remainder != 0m)
        {
            // Weight the uncovered amount by each participant's item subtotal: whoever
            // ordered more of the bill carries more of the tax and tip.
            var weights = raw
                .Select(kv => new SplitInput(kv.Key, kv.Value > 0 ? kv.Value : 1m))
                .ToList();
            foreach (var share in ByWeight(remainder, currency, weights, keepInput: false))
                raw[share.MemberId] = raw.GetValueOrDefault(share.MemberId) + share.Amount;
        }

        // Re-normalise once at the end so the returned shares sum exactly to the total.
        return Reconcile(total, currency, raw.Select(kv => new SplitShare(kv.Key, kv.Value, null)).ToList());
    }

    private static IReadOnlyList<SplitShare> Percentage(decimal total, string currency, IReadOnlyList<SplitInput> inputs)
    {
        var sum = inputs.Sum(i => i.Value ?? 0m);
        if (Math.Abs(sum - 100m) > 0.01m)
            throw new ArgumentException($"Percentages must add up to 100, got {sum}.", nameof(inputs));

        var weighted = inputs.Select(i => new SplitInput(i.MemberId, i.Value ?? 0m)).ToList();
        var shares = ByWeight(total, currency, weighted, keepInput: false);
        return shares
            .Select(s => s with { InputValue = inputs.First(i => i.MemberId == s.MemberId).Value })
            .ToList();
    }

    private static IReadOnlyList<SplitShare> Exact(decimal total, string currency, IReadOnlyList<SplitInput> inputs)
    {
        var sum = inputs.Sum(i => i.Value ?? 0m);
        var tolerance = CurrencyPrecision.MinorUnit(currency) / 2m;
        if (Math.Abs(sum - total) > tolerance)
            throw new ArgumentException(
                $"Exact amounts must add up to the expense total {total}, got {sum}.", nameof(inputs));

        var shares = inputs
            .Select(i => new SplitShare(
                i.MemberId, CurrencyPrecision.RoundStored(i.Value ?? 0m, currency), i.Value))
            .ToList();

        return Reconcile(total, currency, shares);
    }

    /// <summary>
    /// Weighted split with largest-remainder rounding: floor everyone to a whole
    /// unit, then hand the leftover units out one at a time to the largest fractional
    /// parts. Ties break on member id so two devices computing the same split offline
    /// produce byte-identical results.
    ///
    /// The unit is the stored one rather than the currency's, which is what keeps an
    /// even split even: half of 66.13 is 33.065, and a tie at the cent has to break
    /// somewhere - always towards the same member, since the tie-break has to be
    /// deterministic. That is half a cent of drift per expense, all of it in one
    /// direction, and it added up to 71 cents against the app a real group's history
    /// came from.
    /// </summary>
    private static IReadOnlyList<SplitShare> ByWeight(
        decimal total, string currency, IReadOnlyList<SplitInput> inputs, bool keepInput)
    {
        var weights = inputs.Select(i => i.Value ?? 0m).ToArray();
        var weightSum = weights.Sum();
        if (weightSum <= 0m)
            throw new ArgumentException("Split weights must add up to more than zero.", nameof(inputs));

        var unit = CurrencyPrecision.StoredUnit(currency);
        var sign = total < 0 ? -1m : 1m;
        var absTotal = Math.Abs(total);
        var totalUnits = (long)Math.Round(absTotal / unit, MidpointRounding.AwayFromZero);

        var baseUnits = new long[inputs.Count];
        var fractions = new decimal[inputs.Count];
        long assigned = 0;

        for (var i = 0; i < inputs.Count; i++)
        {
            var exact = totalUnits * (weights[i] / weightSum);
            var floor = (long)Math.Floor(exact);
            baseUnits[i] = floor;
            fractions[i] = exact - floor;
            assigned += floor;
        }

        var leftover = totalUnits - assigned;
        var order = Enumerable.Range(0, inputs.Count)
            .OrderByDescending(i => fractions[i])
            .ThenBy(i => inputs[i].MemberId)
            .ToArray();

        for (var k = 0; k < leftover; k++)
            baseUnits[order[k % order.Length]]++;

        return inputs
            .Select((input, i) => new SplitShare(
                input.MemberId,
                sign * baseUnits[i] * unit,
                keepInput ? input.Value : null))
            .ToList();
    }

    /// <summary>
    /// Pushes any residue onto the largest share. Used after paths that build
    /// amounts additively, where a single trailing minor unit can survive.
    /// </summary>
    private static IReadOnlyList<SplitShare> Reconcile(
        decimal total, string currency, IReadOnlyList<SplitShare> shares)
    {
        var rounded = shares
            .Select(s => s with { Amount = CurrencyPrecision.RoundStored(s.Amount, currency) })
            .ToList();

        var residue = CurrencyPrecision.RoundStored(total - rounded.Sum(s => s.Amount), currency);
        if (residue == 0m) return rounded;

        var targetIndex = Enumerable.Range(0, rounded.Count)
            .OrderByDescending(i => Math.Abs(rounded[i].Amount))
            .ThenBy(i => rounded[i].MemberId)
            .First();

        rounded[targetIndex] = rounded[targetIndex] with
        {
            Amount = CurrencyPrecision.RoundStored(rounded[targetIndex].Amount + residue, currency)
        };
        return rounded;
    }
}
