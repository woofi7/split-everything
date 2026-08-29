namespace SplitEverything.Domain.Entities;

/// <summary>
/// One committed import, so a bad run can be identified and undone as a unit.
///
/// Deliberately holds no statement content: the bank statement importer runs
/// entirely client-side and only ever posts confirmed expense records.
/// </summary>
public class ImportBatch
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid? GroupId { get; set; }
    public Guid ImportedByUserId { get; set; }

    /// <summary>"settleup-csv" or "statement".</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>File name only, for the user's own reference. Never the file itself.</summary>
    public string? SourceLabel { get; set; }

    public int ExpenseCount { get; set; }
    public int SkippedCount { get; set; }
    public DateTimeOffset CommittedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RolledBackAt { get; set; }
}
