using SplitEverything.Application.Contracts.Auth;

namespace SplitEverything.Application.Services;

public interface IAuthService
{
    Task<SignInResult> SignInWithGoogleAsync(GoogleSignInRequest request, CancellationToken ct = default);

    Task<SignInResult> SignInAsDeveloperAsync(DevelopmentSignInRequest request, CancellationToken ct = default);

    AuthCapabilities GetCapabilities();

    Task<AuthTokens> RefreshAsync(RefreshRequest request, CancellationToken ct = default);

    Task SignOutAsync(string refreshToken, CancellationToken ct = default);
    Task SignOutAllDevicesAsync(Guid userId, CancellationToken ct = default);

    Task<AuthenticatedUser> GetMeAsync(Guid userId, CancellationToken ct = default);
    Task<AuthenticatedUser> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct = default);

    Task<string> ExportMyDataAsync(Guid userId, CancellationToken ct = default);

    Task DeleteMyAccountAsync(Guid userId, CancellationToken ct = default);
}
