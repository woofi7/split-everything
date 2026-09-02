namespace SplitEverything.Domain.Common;

/// <summary>
/// Minor-unit precision per currency. Needed by every rounding path: splitting
/// 10 JPY three ways must not produce fractions of a yen.
/// </summary>
public static class CurrencyPrecision
{
    private static readonly Dictionary<string, int> Overrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BIF"] = 0, ["CLP"] = 0, ["DJF"] = 0, ["GNF"] = 0, ["ISK"] = 0,
        ["JPY"] = 0, ["KMF"] = 0, ["KRW"] = 0, ["PYG"] = 0, ["RWF"] = 0,
        ["UGX"] = 0, ["UYI"] = 0, ["VND"] = 0, ["VUV"] = 0, ["XAF"] = 0,
        ["XOF"] = 0, ["XPF"] = 0,
        ["BHD"] = 3, ["IQD"] = 3, ["JOD"] = 3, ["KWD"] = 3, ["LYD"] = 3,
        ["OMR"] = 3, ["TND"] = 3
    };

    public const int Default = 2;

    public static int DecimalsFor(string? currency)
        => currency is not null && Overrides.TryGetValue(currency, out var d) ? d : Default;

    /// <summary>Smallest representable amount in the currency, e.g. 0.01 for CAD.</summary>
    public static decimal MinorUnit(string? currency)
    {
        var decimals = DecimalsFor(currency);
        return decimals switch
        {
            0 => 1m,
            1 => 0.1m,
            2 => 0.01m,
            3 => 0.001m,
            _ => (decimal)Math.Pow(10, -decimals)
        };
    }

    public static decimal Round(decimal amount, string? currency)
        => Math.Round(amount, DecimalsFor(currency), MidpointRounding.ToEven);

    /// <summary>
    /// The precision a share is worked out and stored at, which is finer than the
    /// currency people pay in.
    ///
    /// A share is not a payment. Half of 66.13 is 33.065, and forcing that to a cent
    /// hands somebody the extra half-cent every time - always the same somebody,
    /// because the tie has to break somehow. Over four hundred expenses in one real
    /// group that came to 71 cents of drift against the app the history was imported
    /// from, which computes at full precision and rounds only to show a number.
    ///
    /// Two extra decimals, because that is what the amount columns hold, and it is
    /// enough: the worst case left is a hundredth of a cent per expense.
    ///
    /// Currencies with no sub-unit are the exception and keep whole units. A third of
    /// a yen is not a share of anything, and three shares of 3.3333 under a total of
    /// 10 would read as a whole yen gone missing rather than a rounding choice.
    /// </summary>
    public static int StoredDecimalsFor(string? currency)
    {
        var decimals = DecimalsFor(currency);
        return decimals == 0 ? 0 : Math.Min(4, decimals + 2);
    }

    /// <summary>Smallest amount a share can be worked out in, e.g. 0.0001 for CAD.</summary>
    public static decimal StoredUnit(string? currency)
        => StoredDecimalsFor(currency) switch
        {
            0 => 1m,
            1 => 0.1m,
            2 => 0.01m,
            3 => 0.001m,
            4 => 0.0001m,
            var decimals => (decimal)Math.Pow(10, -decimals)
        };

    public static decimal RoundStored(decimal amount, string? currency)
        => Math.Round(amount, StoredDecimalsFor(currency), MidpointRounding.ToEven);
}
