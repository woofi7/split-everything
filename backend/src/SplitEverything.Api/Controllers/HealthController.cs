using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SplitEverything.Infrastructure.Persistence;

namespace SplitEverything.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/health")]
public sealed class HealthController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public IActionResult Live() => Ok(new
    {
        status = "ok",
        version = Environment.GetEnvironmentVariable("APP_VERSION") ?? "dev"
    });

    [HttpGet("ready")]
    public async Task<IActionResult> Ready(CancellationToken ct)
    {
        var reachable = await db.Database.CanConnectAsync(ct);
        return reachable
            ? Ok(new { status = "ready" })
            : StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "database-unreachable" });
    }
}
