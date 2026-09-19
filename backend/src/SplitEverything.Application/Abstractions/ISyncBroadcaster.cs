using SplitEverything.Application.Contracts.Sync;

namespace SplitEverything.Application.Abstractions;

public interface ISyncBroadcaster
{
    Task BroadcastAsync(Guid groupId, SyncPushResult result, string? originDeviceId, CancellationToken ct = default);
    Task NotifyConflictAsync(Guid groupId, Guid userId, SyncConflictDto conflict, CancellationToken ct = default);
}
