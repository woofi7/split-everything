namespace SplitEverything.Application.Contracts.Auth;

public sealed record GoogleSignInRequest(string IdToken, string? DeviceId, string? DeviceLabel, string? Platform);

public sealed record AuthTokens(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public sealed record AuthenticatedUser(
    Guid Id,
    string Email,
    string DisplayName,
    string? AvatarUrl,
    string DefaultCurrency,
    bool PrefersLightTheme);

public sealed record SignInResult(AuthenticatedUser User, AuthTokens Tokens, bool IsNewUser, IReadOnlyList<Guid> AutoJoinedGroupIds);

public sealed record RefreshRequest(string RefreshToken, string? DeviceId);

public sealed record UpdateProfileRequest(string? DisplayName, string? DefaultCurrency, bool? PrefersLightTheme, string? Locale);

/// <summary>
/// Signs in without Google, for local development only. Refused unless it has been
/// deliberately enabled outside production.
/// </summary>
public sealed record DevelopmentSignInRequest(string Email, string? DisplayName, string? DeviceId);

/// <summary>
/// What the sign-in page needs to know before it renders anything, so it can tell
/// "not configured" apart from "broken".
/// </summary>
public sealed record AuthCapabilities(bool GoogleConfigured, bool DevelopmentSignIn);
