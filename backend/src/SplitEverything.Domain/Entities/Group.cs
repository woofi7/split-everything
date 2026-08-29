using SplitEverything.Domain.Common;

namespace SplitEverything.Domain.Entities;

public class Group : SyncableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Base currency. Expenses in other currencies are converted to it for balances.</summary>
    public string BaseCurrency { get; set; } = "CAD";

    public string? EmojiIcon { get; set; }
    public string ColorHex { get; set; } = "#4f46e5";

    /// <summary>Frozen: readable, no new writes accepted.</summary>
    public bool IsArchived { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }

    public Guid CreatedByUserId { get; set; }

    /// <summary>
    /// Server sequence counter handed out to every write in this group. Clients
    /// pull deltas with "give me everything after seq N".
    /// </summary>
    public long SequenceCounter { get; set; }

    /// <summary>
    /// Stable identity of this group's causal history. Preserved across merges and
    /// splits so an entity's lineage can be followed even after the group it was
    /// written in no longer exists.
    /// </summary>
    public Guid LineageId { get; set; } = Guid.CreateVersion7();

    public ICollection<GroupMember> Members { get; set; } = new List<GroupMember>();
    public ICollection<Expense> Expenses { get; set; } = new List<Expense>();
    public ICollection<Settlement> Settlements { get; set; } = new List<Settlement>();
    public ICollection<GroupInvite> Invites { get; set; } = new List<GroupInvite>();
}
