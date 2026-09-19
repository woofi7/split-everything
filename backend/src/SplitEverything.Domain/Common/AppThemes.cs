namespace SplitEverything.Domain.Common;

public static class AppThemes
{
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

    public const string Default = "indigo";

    public static bool IsKnown(string? name)
        => name is not null
           && Names.Any(known => string.Equals(known, name, StringComparison.OrdinalIgnoreCase));

    public static string Normalize(string name)
        => Names.First(known => string.Equals(known, name, StringComparison.OrdinalIgnoreCase));
}
