using Microsoft.AspNetCore.Mvc;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Contracts.Settlements;
using SplitEverything.Application.Services;

namespace SplitEverything.Api.Controllers;

public sealed class SettlementsController(
    ICurrentUser currentUser,
    ISettlementService settlements) : ApiControllerBase(currentUser)
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SettlementDto>>> List(
        [FromQuery] Guid groupId, CancellationToken ct)
        => Ok(await settlements.ListAsync(UserId, groupId, ct));

    [HttpPost]
    public async Task<ActionResult<SettlementDto>> Create(
        CreateSettlementRequest request, CancellationToken ct)
        => Ok(await settlements.CreateAsync(UserId, request, ct));

    [HttpDelete("{settlementId:guid}")]
    public async Task<IActionResult> Delete(Guid settlementId, CancellationToken ct)
    {
        await settlements.DeleteAsync(UserId, settlementId, ct);
        return NoContent();
    }

    /// <summary>Net position across every group, in the caller's own currency.</summary>
    [HttpGet("overall")]
    public async Task<ActionResult<OverallBalanceDto>> Overall(CancellationToken ct)
        => Ok(await settlements.GetOverallBalanceAsync(UserId, ct));

    /// <summary>What the caller and one other person owe each other, group by group.</summary>
    [HttpGet("cross-group")]
    public async Task<ActionResult<CrossGroupBalanceDto>> CrossGroup(
        [FromQuery] Guid withUserId, CancellationToken ct)
        => Ok(await settlements.GetCrossGroupBalanceAsync(UserId, withUserId, ct));

    /// <summary>Cancels the debts that face each other across two groups.</summary>
    [HttpPost("cross-group/offset")]
    public async Task<ActionResult<OffsetAcrossGroupsResult>> Offset(
        OffsetAcrossGroupsRequest request, CancellationToken ct)
        => Ok(await settlements.OffsetAcrossGroupsAsync(UserId, request, ct));

    [HttpPost("nudge")]
    public async Task<IActionResult> Nudge(NudgeRequest request, CancellationToken ct)
    {
        await settlements.NudgeAsync(UserId, request, ct);
        return NoContent();
    }
}
