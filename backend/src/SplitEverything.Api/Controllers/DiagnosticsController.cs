using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SplitEverything.Api.Infrastructure;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Contracts.Diagnostics;

namespace SplitEverything.Api.Controllers;

[ApiController]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Diagnostics)]
[Route("api/diagnostics")]
public sealed class DiagnosticsController(
    ILogger<DiagnosticsController> logger, ICurrentUser currentUser) : ControllerBase
{
    private const int MaxMessage = 500;
    private const int MaxStack = 4000;
    private const int MaxContext = 200;

    [HttpPost("client-error")]
    public IActionResult ClientError(ClientErrorReport report)
    {
        if (string.IsNullOrWhiteSpace(report.Message)) return NoContent();

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

        return NoContent();
    }

    private static string Clamp(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return "-";

        var flat = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return flat.Length <= max ? flat : flat[..max];
    }
}
