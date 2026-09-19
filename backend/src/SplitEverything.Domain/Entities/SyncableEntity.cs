using SplitEverything.Domain.Sync;

namespace SplitEverything.Domain.Entities;

public abstract class SyncableEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string VectorClockJson { get; set; } = "{}";

    public string? LastWriterDeviceId { get; set; }

    public long ServerSeq { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public VectorClock Clock
    {
        get => VectorClock.FromJson(VectorClockJson);
        set => VectorClockJson = value.ToJson();
    }
}
