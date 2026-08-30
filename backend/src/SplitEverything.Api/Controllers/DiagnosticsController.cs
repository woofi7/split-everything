using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SplitEverything.Api.Infrastructure;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Contracts.Diagnostics;

namespace SplitEverything.Api.Controllers;

/// <summary>
/// What went wrong in a browser, written into the server's log.
///
/// A phone is where this app is used and nowhere anybody can read a console. A
/// blank screen on a phone used to be a report of "it broke" and nothing else, so
/// the client now says what it was doing and where, and it lands in the same log as
/// everything else with the same request ids around it.
///
/// Anonymous on purpose: the errors worth reading most are the ones that happen
/// before or instead of signing in. The signed-in caller is recorded when there is
/// one. Rate limited, because a crash loop can report itself very fast.
/// </summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Diagnostics)]
[Route("api/diagnostics")]
public sealed class DiagnosticsController(
    ILogger<DiagnosticsController> logger, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>How much of a report is worth keeping, per field.</summary>
    private const int MaxMessage = 500;
    private const int MaxStack = 4000;
    private const int MaxContext = 200;

    [HttpPost("client-error")]
    public IActionResult ClientError(ClientErrorReport report)
    {
        if (string.IsNullOrWhiteSpace(report.Message)) return NoContent();

        /*
         * Logged as a warning rather than an error: a client fault is not the
         * server failing, and a log full of red for something a phone did makes the
         * server's own errors harder to find. Structured, so the fields can be
         * queried rather than grepped out of a sentence.
         */
        logger.LogWarning(
            "Client error on {ClientRoute}: {ClientMessage} [kind {ClientKind}, user {ClientUserId}, device {ClientDeviceId}, app {ClientVersion}, agent {ClientAgent}] {ClientStack}",
            Clamp(report.Route, MaxContext),
            Clamp(report.Message, MaxMessage),
            Clamp(report.Kind, 40),
            currentUser.UserId?.ToString() ?? "anonymous",
            Clamp(report.DeviceId, MaxContext),
            Clamp(report.AppVersion, 40),
            Clamp(Request.Headers.UserAgent.ToString(), MaxContext),
            Clamp(report.Stack, MaxStack));

        // Nothing to say back. A client that is already broken should not be made to
        // deal with a response, and an error here must never become a second error.
        return NoContent();
    }

    private static string Clamp(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return "-";

        var flat = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return flat.Length <= max ? flat : flat[..max];
    }
}
