namespace SplitEverything.Application.Abstractions;

public sealed record GoogleIdentity(
    string Subject, string Email, bool EmailVerified, string? Name, string? PictureUrl);

public interface IGoogleTokenVerifier
{
    Task<GoogleIdentity> VerifyAsync(string idToken, CancellationToken ct = default);
}
