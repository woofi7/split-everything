using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SplitEverything.Domain.Algorithms;

/// <summary>
/// Stable fingerprint of date + amount + description, used to catch a transaction
/// already in the ledger before an import commits it a second time.
///
/// The description is aggressively normalised because the same purchase reads
/// differently on a bank statement than it did when typed by hand ("UBER EATS
/// 1234 TORONTO ON" vs "Uber Eats"), and card statements carry trailing
/// reference numbers that change per posting.
/// </summary>
public static partial class ExpenseFingerprint
{
    /// <summary>Leading tokens of the description that make up the merchant key.</summary>
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

        // Keep only the leading tokens: the merchant name is at the front and the
        // noise a card statement appends (store number, city, province, terminal
        // id) trails behind it. Two tokens is deliberately aggressive - a duplicate
        // is only ever flagged for review, so over-matching costs the user one
        // glance while under-matching lets a double entry through.
        var tokens = upper.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', tokens.Take(MerchantTokenCount));
    }
}
