using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using SplitEverything.Application.Common;
using SplitEverything.Infrastructure.Auth;

namespace SplitEverything.Tests.Infrastructure;

/// <summary>
/// Google's library does the cryptography; what is worth pinning down here is that
/// we never accept a token we could not verify, and that a missing client id is a
/// configuration error rather than an open door.
/// </summary>
public class GoogleTokenVerifierTests
{
    private static GoogleTokenVerifier Create(string clientId = "test-client-id")
        => new(new AuthOptions { GoogleClientId = clientId },
            NullLogger<GoogleTokenVerifier>.Instance);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_token_is_refused_without_calling_google(string token)
        => await Should.ThrowAsync<ValidationException>(() => Create().VerifyAsync(token));

    [Fact]
    public async Task A_missing_client_id_is_a_configuration_error()
    {
        // Verifying with no audience configured would accept tokens minted for any
        // other application, so this must fail loudly instead of passing.
        await Should.ThrowAsync<InvalidOperationException>(
            () => Create(clientId: string.Empty).VerifyAsync("some.jwt.value"));
    }

    [Fact]
    public async Task A_token_that_is_not_a_jwt_is_refused()
        => await Should.ThrowAsync<ForbiddenException>(() => Create().VerifyAsync("not-a-jwt"));

    [Fact]
    public async Task A_structurally_valid_but_unsigned_token_is_refused()
    {
        // Correct shape, meaningless signature: the only thing standing between an
        // attacker and an account is that this is rejected.
        const string token = "eyJhbGciOiJSUzI1NiIsImtpZCI6ImZha2UifQ." +
                             "eyJzdWIiOiIxMjM0NSIsImVtYWlsIjoiYXR0YWNrZXJAZXhhbXBsZS5jb20iLCJhdWQiOiJ0ZXN0LWNsaWVudC1pZCJ9." +
                             "ZmFrZS1zaWduYXR1cmU";

        await Should.ThrowAsync<ForbiddenException>(() => Create().VerifyAsync(token));
    }
}
