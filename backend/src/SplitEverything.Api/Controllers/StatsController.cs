using Microsoft.AspNetCore.Mvc;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Contracts.Stats;
using SplitEverything.Application.Services;

namespace SplitEverything.Api.Controllers;

public sealed class StatsController(
    ICurrentUser currentUser,
    IStatsService stats) : ApiControllerBase(currentUser)
{
    [HttpGet]
    public async Task<ActionResult<StatsDashboardDto>> Dashboard(
        [FromQuery] StatsQuery query, CancellationToken ct)
        => Ok(await stats.GetDashboardAsync(UserId, query, ct));
}
