namespace SplitEverything.Infrastructure.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>HMAC key for our own access tokens. Must be at least 32 bytes.</summary>
    public string JwtSigningKey { get; set; } = string.Empty;

    public string JwtIssuer { get; set; } = "split-everything";
    public string JwtAudience { get; set; } = "split-everything";

    /// <summary>Short by design: the refresh token is the long-lived credential.</summary>
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;

    /// <summary>Google OAuth client id. Tokens for any other audience are rejected.</summary>
    public string GoogleClientId { get; set; } = string.Empty;

    /// <summary>Public base URL, used to build invite links.</summary>
    public string AppBaseUrl { get; set; } = "http://localhost:5173";

    public string? InviteFromAddress { get; set; }
}
