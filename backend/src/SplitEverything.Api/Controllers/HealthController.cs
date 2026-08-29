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
    /// <summary>Liveness: the process is up. Deliberately does not touch the database.</summary>
    [HttpGet]
    public IActionResult Live() => Ok(new { status = "ok" });

    /// <summary>Readiness: the database is reachable, so Traefik can hold traffic back.</summary>
    [HttpGet("ready")]
    public async Task<IActionResult> Ready(CancellationToken ct)
    {
        var reachable = await db.Database.CanConnectAsync(ct);
        return reachable
            ? Ok(new { status = "ready" })
            : StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "database-unreachable" });
    }
}
