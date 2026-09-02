using System.Buffers.Text;

namespace SplitEverything.Infrastructure.Notifications;

/// <summary>
/// Whether a configured VAPID pair is the shape Web Push requires.
///
/// Checked rather than trusted, because the failure it prevents is silent at the
/// server and loud in the wrong place: a deployment served its contact address as
/// the public key, and every phone that tried to turn notifications on got
/// "Failed to execute 'atob'" from the browser, which says nothing about a server
/// setting being in the wrong slot.
/// </summary>
public static class VapidKey
{
    /// <summary>The uncompressed P-256 point Web Push wants: 0x04, then x, then y.</summary>
    private const int PublicKeyBytes = 65;

    /// <summary>The private key is the 32-byte scalar.</summary>
    private const int PrivateKeyBytes = 32;

    public static bool IsValidPublicKey(string? value) => Decode(value)?.Length == PublicKeyBytes
        && Decode(value)![0] == 0x04;

    public static bool IsValidPrivateKey(string? value) => Decode(value)?.Length == PrivateKeyBytes;

    /// <summary>A contact the push service can reach, which is what a subject is for.</summary>
    public static bool IsValidSubject(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && (value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// base64url, unpadded, the way both the generator and every push library write
    /// it. Padding is accepted too: a key pasted from a tool that adds it is not
    /// wrong, only differently spelled.
    /// </summary>
    private static byte[]? Decode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var trimmed = value.Trim().TrimEnd('=');
        if (trimmed.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))) return null;

        var padded = trimmed.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);

        return Convert.TryFromBase64String(padded, new byte[padded.Length], out var written)
            ? Convert.FromBase64String(padded)[..written]
            : null;
    }
}
