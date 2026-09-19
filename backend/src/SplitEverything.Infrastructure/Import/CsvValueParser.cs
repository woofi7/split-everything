using System.Globalization;

namespace SplitEverything.Infrastructure.Import;

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

        if (value.AsSpan().IndexOfAny(';', '|') >= 0) return null;

        var cleaned = new string(value
            .Where(c => char.IsAsciiDigit(c) || c is '.' or ',' or '-' or '+')
            .ToArray());

        if (cleaned.Length == 0) return null;

        var negative = cleaned.StartsWith('-') || value.Trim().EndsWith('-') || value.Contains('(');
        cleaned = cleaned.TrimStart('-', '+');

        var separator = decimalSeparator;
        if (string.IsNullOrEmpty(separator))
        {
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
