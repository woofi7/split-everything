using SplitEverything.Domain.Common;

namespace SplitEverything.Domain.Entities;

/// <summary>
/// Human-facing audit trail behind the activity feed. Distinct from SyncLog: this
/// is what people read, the sync log is what devices read.
/// </summary>
public class ActivityLogEntry
{
    public long Id { get; set; }

    public Guid? GroupId { get; set; }
    public Group? Group { get; set; }

    public ActivityKind Kind { get; set; }

    public Guid? ActorUserId { get; set; }
    public Guid? ActorMemberId { get; set; }

    public SyncEntityType? SubjectType { get; set; }
    public Guid? SubjectId { get; set; }

    /// <summary>Rendered sentence, built at write time so the feed needs no back-joins.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Extra structured detail for the UI (amounts, member names, revision numbers).</summary>
    public string? MetadataJson { get; set; }

    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
