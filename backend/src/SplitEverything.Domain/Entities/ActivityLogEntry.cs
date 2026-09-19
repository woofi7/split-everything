using SplitEverything.Domain.Common;

namespace SplitEverything.Domain.Entities;

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

    public string Summary { get; set; } = string.Empty;

    public string? MetadataJson { get; set; }

    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
