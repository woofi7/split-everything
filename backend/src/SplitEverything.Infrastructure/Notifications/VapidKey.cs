using System.Buffers.Text;

namespace SplitEverything.Infrastructure.Notifications;

public static class VapidKey
{
    private const int PublicKeyBytes = 65;

    private const int PrivateKeyBytes = 32;

    public static bool IsValidPublicKey(string? value) => Decode(value)?.Length == PublicKeyBytes
        && Decode(value)![0] == 0x04;

    public static bool IsValidPrivateKey(string? value) => Decode(value)?.Length == PrivateKeyBytes;

    public static bool IsValidSubject(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && (value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

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
