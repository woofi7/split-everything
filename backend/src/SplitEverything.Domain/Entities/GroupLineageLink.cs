using SplitEverything.Domain.Common;

namespace SplitEverything.Domain.Entities;

public class GroupLineageLink
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public GroupLineageKind Kind { get; set; }

    public Guid SourceGroupId { get; set; }

    public Guid TargetGroupId { get; set; }

    public Guid MovedLineageId { get; set; }

    public string VectorClockJson { get; set; } = "{}";

    public Guid PerformedByUserId { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Note { get; set; }
}
