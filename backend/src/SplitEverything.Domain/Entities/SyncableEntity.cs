using SplitEverything.Domain.Sync;

namespace SplitEverything.Domain.Entities;

/// <summary>
/// Anything a client may create or edit while offline.
///
/// The sync fields are part of the entity rather than a side table so that a
/// single row read tells us everything needed to decide whether an incoming
/// revision wins, loses, or conflicts.
/// </summary>
public abstract class SyncableEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Serialised vector clock of the revision currently stored.</summary>
    public string VectorClockJson { get; set; } = "{}";

    /// <summary>Device that produced the stored revision.</summary>
    public string? LastWriterDeviceId { get; set; }

    /// <summary>Monotonic per-group sequence, handed out by the server on write.</summary>
    public long ServerSeq { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Tombstone. Rows are never hard-deleted: peers still offline need to learn of the delete.</summary>
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public VectorClock Clock
    {
        get => VectorClock.FromJson(VectorClockJson);
        set => VectorClockJson = value.ToJson();
    }
}
