using SplitEverything.Domain.Common;

namespace SplitEverything.Domain.Entities;

public class SyncConflict
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid GroupId { get; set; }
    public SyncEntityType EntityType { get; set; }
    public Guid EntityId { get; set; }

    public string StoredPayloadJson { get; set; } = "{}";
    public string StoredVectorClockJson { get; set; } = "{}";
    public string? StoredDeviceId { get; set; }

    public string IncomingPayloadJson { get; set; } = "{}";
    public string IncomingVectorClockJson { get; set; } = "{}";
    public string IncomingDeviceId { get; set; } = string.Empty;
    public Guid? IncomingUserId { get; set; }

    public string ConflictingFieldsJson { get; set; } = "[]";

    public ConflictResolution Resolution { get; set; } = ConflictResolution.Unresolved;
    public Guid? ResolvedByUserId { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }

    public DateTimeOffset DetectedAt { get; set; } = DateTimeOffset.UtcNow;
}
