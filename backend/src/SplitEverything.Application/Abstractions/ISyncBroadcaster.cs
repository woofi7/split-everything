using SplitEverything.Application.Contracts.Sync;

namespace SplitEverything.Application.Abstractions;

/// <summary>
/// Live fan-out of accepted operations over SignalR. Offline clients get the same
/// operations later through the delta-batch pull, so this is an optimisation
/// rather than the source of truth.
/// </summary>
public interface ISyncBroadcaster
{
    Task BroadcastAsync(Guid groupId, SyncPushResult result, string? originDeviceId, CancellationToken ct = default);
    Task NotifyConflictAsync(Guid groupId, Guid userId, SyncConflictDto conflict, CancellationToken ct = default);
}
