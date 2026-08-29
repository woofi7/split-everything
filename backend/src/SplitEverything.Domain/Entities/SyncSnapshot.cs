namespace SplitEverything.Domain.Entities;

/// <summary>
/// Yearly compaction artefact: the collapsed state of a group's settled history up
/// to a cutoff, plus the joined vector clock of everything it replaces.
///
/// A device that is further behind than the cutoff bootstraps from the snapshot
/// instead of replaying trimmed entries; a device ahead of it ignores it.
/// </summary>
public class SyncSnapshot
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid GroupId { get; set; }
    public Group? Group { get; set; }

    /// <summary>Every log entry at or below this sequence is represented by the snapshot.</summary>
    public long UpToServerSeq { get; set; }

    /// <summary>Cutoff date used to pick the compacted range.</summary>
    public DateTimeOffset CutoffAt { get; set; }

    /// <summary>Pointwise join of the clocks of every compacted entry.</summary>
    public string VectorClockJson { get; set; } = "{}";

    /// <summary>Collapsed state: surviving entities and settled balances at the cutoff.</summary>
    public string StateJson { get; set; } = "{}";

    public int CompactedEntryCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Set once the compacted entries have actually been trimmed.</summary>
    public DateTimeOffset? TrimmedAt { get; set; }
}
