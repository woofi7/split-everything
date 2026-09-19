namespace SplitEverything.Domain.Entities;

public class Device
{
    public string Id { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public string? Label { get; set; }
    public string Platform { get; set; } = "web";

    public long LastAckedServerSeq { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSyncedAt { get; set; } = DateTimeOffset.UtcNow;
}
