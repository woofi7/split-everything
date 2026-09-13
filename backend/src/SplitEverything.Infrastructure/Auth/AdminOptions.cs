namespace SplitEverything.Infrastructure.Auth;

/// <summary>
/// Who runs this server.
///
/// A self-hosted install has an owner - the person whose machine it is - and that
/// is a different thing from being an owner of a group. They are the one who has
/// to clear out a group somebody abandoned, or look at one they are not in when
/// its members cannot work out what happened to a balance.
///
/// Configured rather than stored, and by address rather than by id: it is a fact
/// about the deployment, it belongs with the connection string and the signing key,
/// and nothing in the application can grant it to anybody. Losing the database does
/// not lose it; gaining the database does not gain it.
/// </summary>
public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    /// <summary>
    /// Email addresses holding administrator rights, matched case-insensitively.
    /// Empty - the default - means this install has no administrator at all, which
    /// is the right answer for one nobody has configured.
    /// </summary>
    public string[] Emails { get; set; } = [];

    public bool Includes(string? email)
        => !string.IsNullOrWhiteSpace(email)
           && Emails.Any(candidate =>
               !string.IsNullOrWhiteSpace(candidate)
               && string.Equals(candidate.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase));
}
