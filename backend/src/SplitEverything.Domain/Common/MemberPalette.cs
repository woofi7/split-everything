namespace SplitEverything.Domain.Common;

/// <summary>
/// The colours a member can be given, and how one is chosen.
///
/// Stored on the member rather than derived from an id, so a group can change it
/// and everyone sees the same thing. Chosen here rather than on a client, because
/// two devices adding two people at once would otherwise both pick the same free
/// colour and neither would know.
///
/// Picked for a dark surface first and checked against the light one, with the
/// hues spread so two people in a group are unlikely to land on neighbours.
/// </summary>
public static class MemberPalette
{
    /// <summary>
    /// The same twelve the client keeps in memberColors.ts, in the same order.
    /// Duplicated rather than served, because a picker that cannot draw itself
    /// until a request comes back is worse than a list in two places. This copy is
    /// the authority: anything outside it is refused.
    /// </summary>
    public static readonly IReadOnlyList<string> Colors =
    [
        "#6366f1", // indigo
        "#f97316", // orange
        "#14b8a6", // teal
        "#ec4899", // pink
        "#84cc16", // lime
        "#8b5cf6", // violet
        "#f59e0b", // amber
        "#06b6d4", // cyan
        "#ef4444", // red
        "#22c55e", // green
        "#a855f7", // purple
        "#eab308"  // yellow
    ];

    /// <summary>Whether a value is one this app would ever have handed out.</summary>
    public static bool IsKnown(string? colorHex)
        => colorHex is not null
           && Colors.Any(colour => string.Equals(colour, colorHex, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The colour for a new member: the first one in the palette that nobody in
    /// the group has.
    ///
    /// A group with more members than the palette has colours has to repeat one,
    /// and repeating in palette order is at least predictable.
    ///
    /// There used to be a wish to honour here, a colour the person preferred in
    /// every group they joined. It was only ever set from one screen, that screen
    /// is gone, and a colour belongs to the group rather than to the person: two
    /// people the same colour in one group defeats the point of having them.
    /// </summary>
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
