using SplitEverything.Domain.Common;

namespace SplitEverything.Domain.Entities;

/// <summary>
/// The operation log devices replicate.
///
/// Shape notes, all of which exist because merge, split and transfer were in scope
/// from the start:
/// - <see cref="ServerSeq"/> is a per-group cursor for cheap delta pulls.
/// - <see cref="VectorClockJson"/> is the causal identity of the operation and is
///   never rewritten, so replaying a merged log preserves happened-before.
/// - <see cref="LineageId"/> identifies the log an operation was written into.
///   After a merge, entries from both sides sit in the same group with different
///   lineage ids, which is what lets a later split partition them again.
/// - <see cref="SupersededBySnapshotId"/> marks entries folded into a compaction
///   snapshot; they can be trimmed once every device has acked past them.
/// </summary>
public class SyncLogEntry
{
    public long Id { get; set; }

    public Guid GroupId { get; set; }
    public Group? Group { get; set; }

    /// <summary>Monotonic within a group. Clients pull "everything after N".</summary>
    public long ServerSeq { get; set; }

    public SyncEntityType EntityType { get; set; }
    public Guid EntityId { get; set; }
    public SyncOperation Operation { get; set; }

    public string DeviceId { get; set; } = string.Empty;
    public Guid? UserId { get; set; }

    public string VectorClockJson { get; set; } = "{}";

    /// <summary>Full entity state after the operation. Snapshots keep replay order-independent.</summary>
    public string PayloadJson { get; set; } = "{}";

    /// <summary>Log this operation was written into. Stable across merge and split.</summary>
    public Guid LineageId { get; set; }

    /// <summary>For Transfer entries: the group the entity came from.</summary>
    public Guid? SourceGroupId { get; set; }

    /// <summary>For Merge/Split marker entries: the other group involved.</summary>
    public Guid? CounterpartGroupId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Guid? SupersededBySnapshotId { get; set; }
    public SyncSnapshot? SupersededBySnapshot { get; set; }
}
