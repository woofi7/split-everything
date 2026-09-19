using SplitEverything.Domain.Common;

namespace SplitEverything.Domain.Entities;

public class Group : SyncableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public string BaseCurrency { get; set; } = "CAD";

    public string? IconName { get; set; }
    public string ColorHex { get; set; } = "#4f46e5";

    public string? ThemeName { get; set; }

    public SplitType DefaultSplitType { get; set; } = SplitType.Equal;

    public string? DefaultSplitValuesJson { get; set; }

    public string? IgnoredNamePatternsJson { get; set; }

    public bool IsArchived { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }

    public Guid CreatedByUserId { get; set; }

    public long SequenceCounter { get; set; }

    public Guid LineageId { get; set; } = Guid.CreateVersion7();

    public ICollection<GroupMember> Members { get; set; } = new List<GroupMember>();
    public ICollection<Expense> Expenses { get; set; } = new List<Expense>();
    public ICollection<Settlement> Settlements { get; set; } = new List<Settlement>();
    public ICollection<GroupInvite> Invites { get; set; } = new List<GroupInvite>();
}
