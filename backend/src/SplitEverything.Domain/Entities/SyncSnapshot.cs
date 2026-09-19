namespace SplitEverything.Domain.Entities;

public class SyncSnapshot
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid GroupId { get; set; }
    public Group? Group { get; set; }

    public long UpToServerSeq { get; set; }

    public DateTimeOffset CutoffAt { get; set; }

    public string VectorClockJson { get; set; } = "{}";

    public string StateJson { get; set; } = "{}";

    public int CompactedEntryCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? TrimmedAt { get; set; }
}
