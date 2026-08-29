using SplitEverything.Domain.Common;

namespace SplitEverything.Domain.Entities;

/// <summary>
/// Edge in the group family tree, written on every merge and split so history can
/// be followed across group boundaries after the fact.
/// </summary>
public class GroupLineageLink
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public GroupLineageKind Kind { get; set; }

    /// <summary>Group the history came from.</summary>
    public Guid SourceGroupId { get; set; }

    /// <summary>Group the history ended up in.</summary>
    public Guid TargetGroupId { get; set; }

    /// <summary>Lineage id carried by the operations that moved.</summary>
    public Guid MovedLineageId { get; set; }

    /// <summary>Joined vector clock at the moment of the operation.</summary>
    public string VectorClockJson { get; set; } = "{}";

    public Guid PerformedByUserId { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Note { get; set; }
}
