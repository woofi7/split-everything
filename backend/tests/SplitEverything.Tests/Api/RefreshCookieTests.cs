using System.Net;
using System.Net.Http.Json;
using NSubstitute;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Contracts.Auth;
using SplitEverything.Tests.Support;
using Shouldly;

namespace SplitEverything.Tests.Api;

/// <summary>
/// The refresh cookie, which is what lets a device come back without being asked.
///
/// It was marked Secure whatever the request, and a browser silently drops a
/// Secure cookie over plain HTTP. A phone reaching a development machine by its
/// LAN address therefore never held a session, and no amount of client work could
/// resume one that was never stored.
/// </summary>
public class RefreshCookieTests(PostgresFixture fixture) : ApiTestBase(fixture)
{
    private async Task<HttpResponseMessage> SignInResponseAsync()
    {
        Factory.Google.VerifyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new GoogleIdentity(
                "google-alice", "alice@example.com", true, "Alice", null)));

        return await Client.PostAsJsonAsync("/api/auth/google",
            new GoogleSignInRequest("google-id-token", TestData.DeviceA, "Test", "web"), Json);
    }

    private static string? CookieHeader(HttpResponseMessage response)
        => response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(v => v.StartsWith("se_refresh", StringComparison.Ordinal))
            : null;

    [Fact]
    public async Task Signing_in_sets_a_refresh_cookie()
    {
        var response = await SignInResponseAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        CookieHeader(response).ShouldNotBeNull();
    }

    [Fact]
    public async Task The_cookie_is_not_marked_secure_over_plain_http()
    {
        // The test host speaks http, the same as a phone on a LAN address.
        var response = await SignInResponseAsync();

        CookieHeader(response)!.ShouldNotContain("secure", Case.Insensitive);
    }

    [Fact]
    public async Task The_cookie_is_always_http_only()
    {
        var response = await SignInResponseAsync();

        // Whatever the scheme, script must never read it.
        CookieHeader(response)!.ShouldContain("httponly", Case.Insensitive);
    }

    [Fact]
    public async Task A_refresh_with_no_body_uses_the_cookie()
    {
        var signIn = await SignInResponseAsync();

        var cookie = CookieHeader(signIn)!.Split(';')[0];

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("X-Device-Id", TestData.DeviceA);

        var response = await Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tokens = await response.Content.ReadFromJsonAsync<AuthTokens>(Json);
        tokens!.AccessToken.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_refresh_with_no_token_at_all_is_refused_without_a_server_error()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("X-Device-Id", TestData.DeviceA);

        var response = await Client.SendAsync(request);

        // The client probes this on every cold start, so it has to be an ordinary
        // refusal rather than a 500.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
