namespace SplitEverything.Domain.Common;

public static class AppLocales
{
    public static readonly IReadOnlyList<string> Tags = ["en", "fr"];

    public const string Default = "en";

    public static bool IsKnown(string? tag)
        => tag is not null
           && Tags.Any(known => string.Equals(known, tag, StringComparison.OrdinalIgnoreCase));

    public static string? Resolve(string? tag)
    {
        if (tag is null) return null;

        var language = tag.Trim().Split(['-', '_'])[0];
        return Tags.FirstOrDefault(known => string.Equals(known, language, StringComparison.OrdinalIgnoreCase));
    }

    public static string Normalize(string tag) => Resolve(tag) ?? Default;
}
