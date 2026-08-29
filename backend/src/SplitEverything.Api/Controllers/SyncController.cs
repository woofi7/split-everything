using Microsoft.AspNetCore.Mvc;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Contracts.Sync;
using SplitEverything.Application.Services;

namespace SplitEverything.Api.Controllers;

/// <summary>
/// The delta-batch transport offline clients fall back to when they reconnect.
/// SignalR carries the same operations live; this is what makes a dropped
/// connection harmless.
/// </summary>
public sealed class SyncController(
    ICurrentUser currentUser,
    ISyncService sync) : ApiControllerBase(currentUser)
{
    [HttpPost("push")]
    public async Task<ActionResult<SyncPushResult>> Push(SyncPushRequest request, CancellationToken ct)
        => Ok(await sync.PushAsync(UserId, WithDevice(request), ct));

    [HttpPost("pull")]
    public async Task<ActionResult<SyncPullResult>> Pull(SyncPullRequest request, CancellationToken ct)
        => Ok(await sync.PullAsync(UserId,
            string.IsNullOrWhiteSpace(request.DeviceId)
                ? request with { DeviceId = CurrentUser.DeviceId ?? string.Empty }
                : request, ct));

    [HttpPost("ack")]
    public async Task<IActionResult> Acknowledge(
        [FromBody] Dictionary<Guid, long> groupCursors, CancellationToken ct)
    {
        await sync.AcknowledgeAsync(UserId, CurrentUser.DeviceId ?? string.Empty, groupCursors, ct);
        return NoContent();
    }

    [HttpGet("conflicts")]
    public async Task<ActionResult<IReadOnlyList<SyncConflictDto>>> Conflicts(
        [FromQuery] Guid? groupId, CancellationToken ct)
        => Ok(await sync.GetOpenConflictsAsync(UserId, groupId, ct));

    [HttpPost("conflicts/resolve")]
    public async Task<ActionResult<SyncConflictDto>> Resolve(
        ResolveConflictRequest request, CancellationToken ct)
        => Ok(await sync.ResolveConflictAsync(UserId, request, ct));

    private SyncPushRequest WithDevice(SyncPushRequest request)
        => string.IsNullOrWhiteSpace(request.DeviceId)
            ? request with { DeviceId = CurrentUser.DeviceId ?? string.Empty }
            : request;
}
