namespace SplitEverything.Domain.Entities;

public class ImportBatch
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid? GroupId { get; set; }
    public Guid ImportedByUserId { get; set; }

    public string Source { get; set; } = string.Empty;

    public string? SourceLabel { get; set; }

    public int ExpenseCount { get; set; }
    public int SkippedCount { get; set; }
    public DateTimeOffset CommittedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RolledBackAt { get; set; }
}
