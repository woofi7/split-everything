using SplitEverything.Application.Contracts.Auth;

namespace SplitEverything.Application.Services;

public interface IAuthService
{
    /// <summary>
    /// Verifies a Google ID token, upserts the user by Google subject, and issues our
    /// own short-lived access token plus a rotating refresh token. Also claims any
    /// pending invite that was pinned to the verified email address.
    /// </summary>
    Task<SignInResult> SignInWithGoogleAsync(GoogleSignInRequest request, CancellationToken ct = default);

    /// <summary>
    /// Signs in without Google. Refused unless AllowDevelopmentSignIn is on, which
    /// production never sets: this is an authentication bypass and exists only so
    /// the app can be used locally before an OAuth client is registered.
    /// </summary>
    Task<SignInResult> SignInAsDeveloperAsync(DevelopmentSignInRequest request, CancellationToken ct = default);

    /// <summary>What the sign-in page can offer.</summary>
    AuthCapabilities GetCapabilities();

    /// <summary>Rotates the refresh token. Presenting a revoked token kills the whole chain.</summary>
    Task<AuthTokens> RefreshAsync(RefreshRequest request, CancellationToken ct = default);

    Task SignOutAsync(string refreshToken, CancellationToken ct = default);
    Task SignOutAllDevicesAsync(Guid userId, CancellationToken ct = default);

    Task<AuthenticatedUser> GetMeAsync(Guid userId, CancellationToken ct = default);
    Task<AuthenticatedUser> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct = default);

    /// <summary>GDPR-style export of everything tied to this user.</summary>
    Task<string> ExportMyDataAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Erases the user's identity and detaches their history. Group rows survive as
    /// placeholders so other members' balances stay correct.
    /// </summary>
    Task DeleteMyAccountAsync(Guid userId, CancellationToken ct = default);
}
