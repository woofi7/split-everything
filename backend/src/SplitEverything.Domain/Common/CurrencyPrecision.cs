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
}
