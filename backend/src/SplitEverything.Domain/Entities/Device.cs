namespace SplitEverything.Domain.Entities;

/// <summary>
/// A client install. Its id is the key in every vector clock, so it must be
/// stable for the life of the install and never reused.
/// </summary>
public class Device
{
    public string Id { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public string? Label { get; set; }
    public string Platform { get; set; } = "web";

    /// <summary>Highest per-group server sequence this device has acknowledged.</summary>
    public long LastAckedServerSeq { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSyncedAt { get; set; } = DateTimeOffset.UtcNow;
}
