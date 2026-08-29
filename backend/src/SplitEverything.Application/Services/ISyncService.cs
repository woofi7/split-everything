using SplitEverything.Application.Contracts.Sync;

namespace SplitEverything.Application.Services;

public interface ISyncService
{
    /// <summary>
    /// Applies a batch of offline operations. Each is accepted when its clock
    /// dominates the stored one, ignored when it is already contained, and recorded
    /// as a conflict when the two are concurrent - never silently overwritten.
    /// </summary>
    Task<SyncPushResult> PushAsync(Guid userId, SyncPushRequest request, CancellationToken ct = default);

    /// <summary>Delta pull from a per-group cursor, used on reconnect.</summary>
    Task<SyncPullResult> PullAsync(Guid userId, SyncPullRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<SyncConflictDto>> GetOpenConflictsAsync(Guid userId, Guid? groupId = null, CancellationToken ct = default);
    Task<SyncConflictDto> ResolveConflictAsync(Guid userId, ResolveConflictRequest request, CancellationToken ct = default);

    /// <summary>Records that a device has durably applied everything up to a cursor.</summary>
    Task AcknowledgeAsync(Guid userId, string deviceId, IReadOnlyDictionary<Guid, long> groupCursors, CancellationToken ct = default);
}
