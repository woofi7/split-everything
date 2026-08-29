using SplitEverything.Domain.Common;

namespace SplitEverything.Application.Contracts.Sync;

// <summary>One offline mutation, exactly as the client recorded it.</summary>
public sealed record SyncOperationDto(
    Guid OperationId,
    SyncEntityType EntityType,
    Guid EntityId,
    SyncOperation Operation,
    Guid GroupId,
    string PayloadJson,
    IReadOnlyDictionary<string, long> VectorClock,
    DateTimeOffset ClientTimestamp);

public sealed record SyncPushRequest(
    string DeviceId,
    IReadOnlyList<SyncOperationDto> Operations);

public sealed record SyncAcceptedDto(Guid OperationId, Guid EntityId, long ServerSeq, IReadOnlyDictionary<string, long> VectorClock);

public sealed record SyncRejectedDto(Guid OperationId, Guid EntityId, string Reason, string Code);

public sealed record SyncConflictDto(
    Guid ConflictId,
    Guid GroupId,
    SyncEntityType EntityType,
    Guid EntityId,
    string StoredPayloadJson,
    IReadOnlyDictionary<string, long> StoredVectorClock,
    string IncomingPayloadJson,
    IReadOnlyDictionary<string, long> IncomingVectorClock,
    IReadOnlyList<string> ConflictingFields,
    DateTimeOffset DetectedAt);

public sealed record SyncPushResult(
    IReadOnlyList<SyncAcceptedDto> Accepted,
    IReadOnlyList<SyncConflictDto> Conflicts,
    IReadOnlyList<SyncRejectedDto> Rejected,
    IReadOnlyDictionary<Guid, long> GroupCursors);

/// <summary>Cursor-based delta pull, one cursor per group the device follows.</summary>
public sealed record SyncPullRequest(
    string DeviceId,
    IReadOnlyDictionary<Guid, long> GroupCursors,
    int MaxEntries = 500);

public sealed record SyncLogEntryDto(
    long ServerSeq,
    Guid GroupId,
    SyncEntityType EntityType,
    Guid EntityId,
    SyncOperation Operation,
    string DeviceId,
    string PayloadJson,
    IReadOnlyDictionary<string, long> VectorClock,
    Guid LineageId,
    Guid? SourceGroupId,
    Guid? CounterpartGroupId,
    DateTimeOffset CreatedAt);

public sealed record SyncSnapshotDto(
    Guid Id, Guid GroupId, long UpToServerSeq, DateTimeOffset CutoffAt,
    IReadOnlyDictionary<string, long> VectorClock, string StateJson);

public sealed record SyncPullResult(
    IReadOnlyList<SyncLogEntryDto> Entries,
    IReadOnlyDictionary<Guid, long> GroupCursors,
    // A device further behind than a compaction cutoff must bootstrap from these
    // instead of replaying trimmed entries.
    IReadOnlyList<SyncSnapshotDto> Snapshots,
    bool HasMore);

public sealed record ResolveConflictRequest(Guid ConflictId, ConflictResolution Resolution, string? MergedPayloadJson);

public sealed record CompactionResult(Guid GroupId, Guid? SnapshotId, int CompactedEntries, int TrimmedEntries, long UpToServerSeq);
