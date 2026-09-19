namespace SplitEverything.Domain.Common;

public static class MemberPalette
{
    public static readonly IReadOnlyList<string> Colors =
    [
        "#6366f1",
        "#f97316",
        "#14b8a6",
        "#ec4899",
        "#84cc16",
        "#8b5cf6",
        "#f59e0b",
        "#06b6d4",
        "#ef4444",
        "#22c55e",
        "#a855f7",
        "#eab308"
    ];

    public static bool IsKnown(string? colorHex)
        => colorHex is not null
           && Colors.Any(colour => string.Equals(colour, colorHex, StringComparison.OrdinalIgnoreCase));

    public static string Assign(IEnumerable<string?> taken)
    {
        var used = taken
            .Where(colour => colour is not null)
            .Select(colour => colour!.ToLowerInvariant())
            .ToHashSet();

        return Colors.FirstOrDefault(colour => !used.Contains(colour.ToLowerInvariant()))
               ?? Colors[used.Count % Colors.Count];
    }
}
