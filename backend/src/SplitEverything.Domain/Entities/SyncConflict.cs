using SplitEverything.Domain.Common;

namespace SplitEverything.Domain.Entities;

/// <summary>
/// A concurrent edit that could not be ordered. Held for a human to resolve; the
/// server never silently picks a winner.
/// </summary>
public class SyncConflict
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid GroupId { get; set; }
    public SyncEntityType EntityType { get; set; }
    public Guid EntityId { get; set; }

    /// <summary>State currently stored, and the clock it carries.</summary>
    public string StoredPayloadJson { get; set; } = "{}";
    public string StoredVectorClockJson { get; set; } = "{}";
    public string? StoredDeviceId { get; set; }

    /// <summary>State that arrived and lost the race, kept verbatim.</summary>
    public string IncomingPayloadJson { get; set; } = "{}";
    public string IncomingVectorClockJson { get; set; } = "{}";
    public string IncomingDeviceId { get; set; } = string.Empty;
    public Guid? IncomingUserId { get; set; }

    /// <summary>Field names that actually differ; the UI only asks about these.</summary>
    public string ConflictingFieldsJson { get; set; } = "[]";

    public ConflictResolution Resolution { get; set; } = ConflictResolution.Unresolved;
    public Guid? ResolvedByUserId { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }

    public DateTimeOffset DetectedAt { get; set; } = DateTimeOffset.UtcNow;
}
