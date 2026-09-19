namespace SplitEverything.Domain.Entities;

public class ExpenseRevision
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid ExpenseId { get; set; }
    public Expense? Expense { get; set; }

    public Guid GroupId { get; set; }

    public int Revision { get; set; }
    public Guid? EditedByUserId { get; set; }
    public string? EditedByDeviceId { get; set; }
    public DateTimeOffset EditedAt { get; set; } = DateTimeOffset.UtcNow;

    public string VectorClockJson { get; set; } = "{}";

    public string SnapshotJson { get; set; } = "{}";

    public string? ChangeSummary { get; set; }
}
