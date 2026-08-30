using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Contracts.Auth;
using GoogleSignInResult = SplitEverything.Application.Contracts.Auth.SignInResult;
using SplitEverything.Application.Services;

namespace SplitEverything.Api.Controllers;

public sealed class AuthController(
    ICurrentUser currentUser,
    IAuthService auth) : ApiControllerBase(currentUser)
{
    /// <summary>Exchanges a Google ID token for our own tokens.</summary>
    [AllowAnonymous]
    [HttpPost("google")]
    public async Task<ActionResult<GoogleSignInResult>> SignInWithGoogle(
        GoogleSignInRequest request, CancellationToken ct)
    {
        var result = await auth.SignInWithGoogleAsync(WithDeviceHeader(request), ct);
        SetRefreshCookie(result.Tokens);
        return Ok(result);
    }

    /// <summary>
    /// Signs in with an email address and no Google account.
    ///
    /// Only reachable when the flag is on, which startup forces off outside
    /// Development. It exists so a fresh clone is usable before an OAuth client
    /// has been registered.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("dev")]
    public async Task<ActionResult<GoogleSignInResult>> SignInAsDeveloper(
        DevelopmentSignInRequest request, CancellationToken ct)
    {
        var result = await auth.SignInAsDeveloperAsync(
            request.DeviceId is null ? request with { DeviceId = CurrentUser.DeviceId } : request, ct);

        SetRefreshCookie(result.Tokens);
        return Ok(result);
    }

    /// <summary>What the sign-in page can offer, so it can explain itself.</summary>
    [AllowAnonymous]
    [HttpGet("capabilities")]
    public ActionResult<AuthCapabilities> Capabilities() => Ok(auth.GetCapabilities());

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<ActionResult<AuthTokens>> Refresh(RefreshRequest? request, CancellationToken ct)
    {
        // The browser keeps the refresh token in an httpOnly cookie; the native
        // shells post it in the body.
        var token = request?.RefreshToken;
        if (string.IsNullOrWhiteSpace(token))
            Request.Cookies.TryGetValue(RefreshCookieName, out token);

        var tokens = await auth.RefreshAsync(
            new RefreshRequest(token ?? string.Empty, CurrentUser.DeviceId), ct);

        SetRefreshCookie(tokens);
        return Ok(tokens);
    }

    [AllowAnonymous]
    [HttpPost("signout")]
    public async Task<IActionResult> SignOutCurrent(RefreshRequest? request, CancellationToken ct)
    {
        var token = request?.RefreshToken;
        if (string.IsNullOrWhiteSpace(token))
            Request.Cookies.TryGetValue(RefreshCookieName, out token);

        if (!string.IsNullOrWhiteSpace(token)) await auth.SignOutAsync(token, ct);

        Response.Cookies.Delete(RefreshCookieName);
        return NoContent();
    }

    [HttpPost("signout-all")]
    public async Task<IActionResult> SignOutEverywhere(CancellationToken ct)
    {
        await auth.SignOutAllDevicesAsync(UserId, ct);
        Response.Cookies.Delete(RefreshCookieName);
        return NoContent();
    }

    [HttpGet("me")]
    public async Task<ActionResult<AuthenticatedUser>> Me(CancellationToken ct)
        => Ok(await auth.GetMeAsync(UserId, ct));

    [HttpPatch("me")]
    public async Task<ActionResult<AuthenticatedUser>> UpdateMe(
        UpdateProfileRequest request, CancellationToken ct)
        => Ok(await auth.UpdateProfileAsync(UserId, request, ct));

    /// <summary>Everything we hold about the caller, as a downloadable file.</summary>
    [HttpGet("me/export")]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var json = await auth.ExportMyDataAsync(UserId, ct);
        return File(System.Text.Encoding.UTF8.GetBytes(json),
            "application/json", $"split-everything-export-{DateTime.UtcNow:yyyy-MM-dd}.json");
    }

    [HttpDelete("me")]
    public async Task<IActionResult> DeleteMe(CancellationToken ct)
    {
        await auth.DeleteMyAccountAsync(UserId, ct);
        Response.Cookies.Delete(RefreshCookieName);
        return NoContent();
    }

    private const string RefreshCookieName = "se_refresh";

    private GoogleSignInRequest WithDeviceHeader(GoogleSignInRequest request)
        => string.IsNullOrWhiteSpace(request.DeviceId)
            ? request with { DeviceId = CurrentUser.DeviceId }
            : request;

    private void SetRefreshCookie(AuthTokens tokens)
        => Response.Cookies.Append(RefreshCookieName, tokens.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            // Marked Secure only when the request arrived over one. A browser
            // silently drops a Secure cookie sent over plain HTTP, which is how a
            // phone testing against a LAN address is reached, so setting it
            // unconditionally meant that device never held a session at all.
            // Production is behind TLS, so this is Secure there.
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Expires = tokens.RefreshTokenExpiresAt,
            Path = "/"
        });
}
