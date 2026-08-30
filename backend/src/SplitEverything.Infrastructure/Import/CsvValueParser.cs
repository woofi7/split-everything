using System.Globalization;

namespace SplitEverything.Infrastructure.Import;

/// <summary>
/// Locale-tolerant parsing of the two fields that actually vary: dates and amounts.
/// Both accept a caller-supplied hint first, then fall back to a list of formats
/// real exports have been seen to use.
/// </summary>
public static class CsvValueParser
{
    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd", "yyyy/MM/dd", "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm:ss",
        "dd.MM.yyyy", "dd/MM/yyyy", "MM/dd/yyyy", "d.M.yyyy", "d/M/yyyy",
        "dd-MM-yyyy", "MMM d, yyyy", "d MMM yyyy"
    ];

    public static DateTimeOffset? ParseDate(string? value, string? format)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();

        if (!string.IsNullOrWhiteSpace(format)
            && DateTime.TryParseExact(trimmed, format, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var exact))
        {
            return new DateTimeOffset(exact, TimeSpan.Zero);
        }

        if (DateTime.TryParseExact(trimmed, DateFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var known))
        {
            return new DateTimeOffset(known, TimeSpan.Zero);
        }

        if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var loose))
        {
            return new DateTimeOffset(loose, TimeSpan.Zero);
        }

        return null;
    }

    public static decimal? ParseAmount(string? value, string? decimalSeparator)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        // A cell holding a list is not a number, and must not be turned into one.
        // Settle Up writes a shared payment as "40;25" - two people paying 40 and
        // 25 - and stripping the separator the way the cleaning below strips a
        // currency symbol read that as four thousand and twenty-five. It went into
        // a real import and put the group's total out by 3,960. The caller asks
        // ParseAmountList when a list is possible.
        if (value.AsSpan().IndexOfAny(';', '|') >= 0) return null;

        // Strip currency symbols, spaces and non-breaking spaces used as group
        // separators, keeping only digits, separators and a sign.
        var cleaned = new string(value
            .Where(c => char.IsAsciiDigit(c) || c is '.' or ',' or '-' or '+')
            .ToArray());

        if (cleaned.Length == 0) return null;

        // A negative sign may trail the number on some statements.
        var negative = cleaned.StartsWith('-') || value.Trim().EndsWith('-') || value.Contains('(');
        cleaned = cleaned.TrimStart('-', '+');

        var separator = decimalSeparator;
        if (string.IsNullOrEmpty(separator))
        {
            // Whichever of . or , appears last is the decimal point; the other is a
            // thousands separator.
            var lastDot = cleaned.LastIndexOf('.');
            var lastComma = cleaned.LastIndexOf(',');
            separator = lastComma > lastDot ? "," : ".";
        }

        var thousands = separator == "," ? "." : ",";
        cleaned = cleaned.Replace(thousands, string.Empty).Replace(separator, ".");

        if (!decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            return null;

        return negative ? -amount : amount;
    }

    /// <summary>
    /// Splits a per-person amount cell such as "209.43;209.43".
    ///
    /// Order is meaningful: these line up with the participant names, so nothing is
    /// deduplicated or reordered the way names are. A cell that does not parse
    /// cleanly returns nothing, and the caller computes the split instead of
    /// trusting half an answer.
    /// </summary>
    public static IReadOnlyList<decimal> ParseAmountList(string? value, string? decimalSeparator)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];

        var parts = value.Split([';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var amounts = new List<decimal>(parts.Length);

        foreach (var part in parts)
        {
            var parsed = ParseAmount(part, decimalSeparator);
            if (parsed is null) return [];
            amounts.Add(parsed.Value);
        }

        return amounts;
    }

    /// <summary>Splits a participant cell such as "Alice, Bob" or "Alice; Bob".</summary>
    public static IReadOnlyList<string> ParseNameList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];

        return value
            .Split([',', ';', '|', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
