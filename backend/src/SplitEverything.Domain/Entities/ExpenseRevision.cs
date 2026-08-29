namespace SplitEverything.Domain.Entities;

/// <summary>
/// Full edit history for an expense: one row per revision, holding the JSON
/// snapshot of the expense and its splits as they stood after that edit.
///
/// Snapshots rather than diffs, because an expense travels between groups with its
/// history and a diff chain would break the moment the log around it is
/// repartitioned.
/// </summary>
public class ExpenseRevision
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid ExpenseId { get; set; }
    public Expense? Expense { get; set; }

    /// <summary>Group the expense belonged to at the time of this revision.</summary>
    public Guid GroupId { get; set; }

    public int Revision { get; set; }
    public Guid? EditedByUserId { get; set; }
    public string? EditedByDeviceId { get; set; }
    public DateTimeOffset EditedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Vector clock of the revision, so history stays orderable after a merge.</summary>
    public string VectorClockJson { get; set; } = "{}";

    public string SnapshotJson { get; set; } = "{}";

    /// <summary>Short human-readable summary for the activity feed.</summary>
    public string? ChangeSummary { get; set; }
}
