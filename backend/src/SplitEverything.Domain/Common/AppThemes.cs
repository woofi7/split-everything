namespace SplitEverything.Domain.Common;

/// <summary>
/// The accent colours the whole application can wear.
///
/// A name rather than a colour, because the client turns one name into the three
/// shades a theme needs, and a stored hex would pin two of them down forever.
/// Kept on the account rather than on the device: someone who picks a colour means
/// it wherever they sign in.
/// </summary>
public static class AppThemes
{
    /// <summary>
    /// The same eight the client keeps in themes.ts, in the same order. Duplicated
    /// rather than served, because a picker that cannot draw itself until a request
    /// comes back is worse than a list in two places. This copy is the authority:
    /// anything outside it is refused.
    /// </summary>
    public static readonly IReadOnlyList<string> Names =
    [
        "indigo",
        "violet",
        "sky",
        "teal",
        "green",
        "amber",
        "rose",
        "slate"
    ];

    /// <summary>What an account with no preference of its own wears.</summary>
    public const string Default = "indigo";

    /// <summary>Whether a value is a theme this app would ever have offered.</summary>
    public static bool IsKnown(string? name)
        => name is not null
           && Names.Any(known => string.Equals(known, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The stored spelling of a name, so case cannot fork the value.</summary>
    public static string Normalize(string name)
        => Names.First(known => string.Equals(known, name, StringComparison.OrdinalIgnoreCase));
}
