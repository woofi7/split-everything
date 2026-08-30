namespace SplitEverything.Domain.Common;

/// <summary>
/// The languages the app is written in.
///
/// A tag rather than a name, and only the two: the strings exist in English and in
/// French and in nothing else, so accepting "de" would leave somebody reading a
/// half-translated screen with no way back.
/// </summary>
public static class AppLocales
{
    public static readonly IReadOnlyList<string> Tags = ["en", "fr"];

    public const string Default = "en";

    /// <summary>Whether this is a language the app can actually be read in.</summary>
    public static bool IsKnown(string? tag)
        => tag is not null
           && Tags.Any(known => string.Equals(known, tag, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The language a tag asks for, or nothing when the app has no strings for it.
    ///
    /// A regional tag is taken for its language: a browser offering fr-CA means
    /// French, and refusing that would be pedantry. Anything else answers nothing,
    /// so a caller can refuse it rather than silently storing something else.
    /// </summary>
    public static string? Resolve(string? tag)
    {
        if (tag is null) return null;

        var language = tag.Trim().Split(['-', '_'])[0];
        return Tags.FirstOrDefault(known => string.Equals(known, language, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The stored spelling of a tag, for reading a value back. Anything unknown
    /// reads as the default rather than leaving a screen with no language at all.
    /// </summary>
    public static string Normalize(string tag) => Resolve(tag) ?? Default;
}
