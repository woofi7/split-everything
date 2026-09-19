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
    bool PrefersLightTheme,
    string? ThemeName,
    string Locale,
    bool IsAdmin = false);

public sealed record SignInResult(AuthenticatedUser User, AuthTokens Tokens, bool IsNewUser, IReadOnlyList<Guid> AutoJoinedGroupIds);

public sealed record RefreshRequest(string RefreshToken, string? DeviceId);

public sealed record UpdateProfileRequest(
    string? DisplayName,
    string? DefaultCurrency,
    bool? PrefersLightTheme,
    string? Locale = null,
    string? ThemeName = null);

public sealed record DevelopmentSignInRequest(string Email, string? DisplayName, string? DeviceId);

public sealed record AuthCapabilities(bool GoogleConfigured, bool DevelopmentSignIn);
