using SplitEverything.Domain.Common;

namespace SplitEverything.Domain.Entities;

public class SyncLogEntry
{
    public long Id { get; set; }

    public Guid GroupId { get; set; }
    public Group? Group { get; set; }

    public long ServerSeq { get; set; }

    public SyncEntityType EntityType { get; set; }
    public Guid EntityId { get; set; }
    public SyncOperation Operation { get; set; }

    public string DeviceId { get; set; } = string.Empty;
    public Guid? UserId { get; set; }

    public string VectorClockJson { get; set; } = "{}";

    public string PayloadJson { get; set; } = "{}";

    public Guid LineageId { get; set; }

    public Guid? SourceGroupId { get; set; }

    public Guid? CounterpartGroupId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Guid? SupersededBySnapshotId { get; set; }
    public SyncSnapshot? SupersededBySnapshot { get; set; }
}
