using SplitEverything.Application.Contracts.Sync;

namespace SplitEverything.Application.Services;

public interface ISyncService
{
    Task<SyncPushResult> PushAsync(Guid userId, SyncPushRequest request, CancellationToken ct = default);

    Task<SyncPullResult> PullAsync(Guid userId, SyncPullRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<SyncConflictDto>> GetOpenConflictsAsync(Guid userId, Guid? groupId = null, CancellationToken ct = default);
    Task<SyncConflictDto> ResolveConflictAsync(Guid userId, ResolveConflictRequest request, CancellationToken ct = default);

    Task AcknowledgeAsync(Guid userId, string deviceId, IReadOnlyDictionary<Guid, long> groupCursors, CancellationToken ct = default);
}
