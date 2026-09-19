namespace SplitEverything.Infrastructure.Auth;

public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    public string[] Emails { get; set; } = [];

    public bool Includes(string? email)
        => !string.IsNullOrWhiteSpace(email)
           && Emails.Any(candidate =>
               !string.IsNullOrWhiteSpace(candidate)
               && string.Equals(candidate.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase));
}
