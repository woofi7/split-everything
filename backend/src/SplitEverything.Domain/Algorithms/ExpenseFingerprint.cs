using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SplitEverything.Domain.Algorithms;

public static partial class ExpenseFingerprint
{
    public const int MerchantTokenCount = 2;

    [GeneratedRegex(@"[^A-Z0-9 ]", RegexOptions.CultureInvariant)]
    private static partial Regex NonAlnum();

    [GeneratedRegex(@"\b\d{3,}\b", RegexOptions.CultureInvariant)]
    private static partial Regex LongDigits();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    public static string Compute(DateTimeOffset date, decimal amount, string currency, string description)
    {
        var payload = string.Join('|',
            date.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Math.Abs(amount).ToString("0.00", CultureInfo.InvariantCulture),
            currency.ToUpperInvariant(),
            NormalizeDescription(description));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexStringLower(hash)[..32];
    }

    public static string NormalizeDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description)) return string.Empty;

        var upper = description.ToUpperInvariant();
        upper = NonAlnum().Replace(upper, " ");
        upper = LongDigits().Replace(upper, " ");
        upper = Whitespace().Replace(upper, " ").Trim();

        var tokens = upper.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', tokens.Take(MerchantTokenCount));
    }
}
