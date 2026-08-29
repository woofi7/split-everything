namespace SplitEverything.Application.Abstractions;

public sealed record GoogleIdentity(
    string Subject, string Email, bool EmailVerified, string? Name, string? PictureUrl);

public interface IGoogleTokenVerifier
{
    /// <summary>Validates signature, audience and expiry. Throws on anything unacceptable.</summary>
    Task<GoogleIdentity> VerifyAsync(string idToken, CancellationToken ct = default);
}
