using Google.Apis.Auth;
using Microsoft.Extensions.Logging;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Common;

namespace SplitEverything.Infrastructure.Auth;

public sealed class GoogleTokenVerifier(AuthOptions options, ILogger<GoogleTokenVerifier> logger)
    : IGoogleTokenVerifier
{
    public async Task<GoogleIdentity> VerifyAsync(string idToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idToken))
            throw new ValidationException("A Google ID token is required.");
        if (string.IsNullOrWhiteSpace(options.GoogleClientId))
            throw new InvalidOperationException("Auth:GoogleClientId is not configured.");

        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = [options.GoogleClientId]
            });

            return new GoogleIdentity(
                payload.Subject,
                payload.Email ?? string.Empty,
                payload.EmailVerified,
                payload.Name,
                payload.Picture);
        }
        catch (InvalidJwtException ex)
        {
            logger.LogWarning(ex, "Rejected a Google ID token");
            throw new ForbiddenException("That Google sign-in could not be verified.");
        }
    }
}
