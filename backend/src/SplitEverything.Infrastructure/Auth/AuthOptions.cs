namespace SplitEverything.Infrastructure.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public string JwtSigningKey { get; set; } = string.Empty;

    public string JwtIssuer { get; set; } = "split-everything";
    public string JwtAudience { get; set; } = "split-everything";

    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;

    public string GoogleClientId { get; set; } = string.Empty;

    public string AppBaseUrl { get; set; } = "http://localhost:5173";

    public string? InviteFromAddress { get; set; }

    public bool AllowDevelopmentSignIn { get; set; }
}
