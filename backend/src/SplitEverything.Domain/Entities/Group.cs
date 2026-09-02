using SplitEverything.Domain.Common;

namespace SplitEverything.Domain.Entities;

public class Group : SyncableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Base currency. Expenses in other currencies are converted to it for balances.</summary>
    public string BaseCurrency { get; set; } = "CAD";

    /// <summary>Font Awesome solid icon name, such as "house". Null for none.</summary>
    public string? IconName { get; set; }
    public string ColorHex { get; set; } = "#4f46e5";

    /// <summary>
    /// How a new expense in this group is split unless someone says otherwise.
    ///
    /// A group setting rather than a device preference: how a household divides
    /// its costs is a fact about the household, and it should hold on whichever
    /// phone the next expense is typed on.
    /// </summary>
    public SplitType DefaultSplitType { get; set; } = SplitType.Equal;

    /// <summary>
    /// Per-member values for the default split, as member id to weight, or null
    /// when the default needs none. Stored as JSON because the shape is a map
    /// keyed by rows in another table, and a table of its own would buy nothing:
    /// it is only ever read and written whole.
    /// </summary>
    public string? DefaultSplitValuesJson { get; set; }

    /// <summary>
    /// Patterns for expenses to leave out of the highlights, as a JSON array of
    /// regular expressions. Null or empty when a group wants none.
    ///
    /// A household with rent in it has one expense every month that is larger than
    /// everything else put together, and "the biggest thing you bought in August"
    /// answering "the rent" every time is a fact nobody needed telling. These say
    /// which names to skip when picking that out.
    ///
    /// They never change what anything cost. Totals, balances and who owes whom are
    /// money that moved, and a display rule has no business touching them - a group
    /// total that quietly disagrees with the expenses under it is a bug report, not
    /// a feature.
    /// </summary>
    public string? IgnoredNamePatternsJson { get; set; }

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
