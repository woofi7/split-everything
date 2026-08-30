using SplitEverything.Domain.Common;

namespace SplitEverything.Domain.Entities;

public class Expense : SyncableEntity
{
    public Guid GroupId { get; set; }
    public Group? Group { get; set; }

    /// <summary>Group member who fronted the money.</summary>
    public Guid PaidByMemberId { get; set; }
    public GroupMember? PaidByMember { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>Amount as entered, in <see cref="Currency"/>.</summary>
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "CAD";

    /// <summary>
    /// Amount converted into the group's base currency, and the rate used. Frozen at
    /// entry time so a later FX move never rewrites settled history.
    /// </summary>
    public decimal AmountInBaseCurrency { get; set; }
    public decimal ExchangeRate { get; set; } = 1m;
    public DateTimeOffset? ExchangeRateAsOf { get; set; }

    public DateTimeOffset SpentAt { get; set; } = DateTimeOffset.UtcNow;


    public SplitType SplitType { get; set; } = SplitType.Equal;

    public Guid? ReceiptId { get; set; }
    public Receipt? Receipt { get; set; }

    public string? Notes { get; set; }

    /// <summary>Set when this expense is an occurrence generated from a recurrence rule.</summary>
    public Guid? RecurringExpenseId { get; set; }
    public RecurringExpense? RecurringExpense { get; set; }

    /// <summary>
    /// Group this expense was originally created in, if it has been transferred.
    /// Kept so a transferred expense's audit trail still points back at its origin.
    /// </summary>
    public Guid? OriginGroupId { get; set; }

    /// <summary>Lineage of the group log this expense was born in. Survives merge, split and transfer.</summary>
    public Guid OriginLineageId { get; set; }

    /// <summary>Bumped on every edit; the activity feed renders one entry per revision.</summary>
    public int Revision { get; set; } = 1;

    /// <summary>Set when a statement or CSV import created this row, for dedupe and traceability.</summary>
    public string? ImportFingerprint { get; set; }
    public Guid? ImportBatchId { get; set; }

    public ICollection<ExpenseSplit> Splits { get; set; } = new List<ExpenseSplit>();
    public ICollection<ExpenseItem> Items { get; set; } = new List<ExpenseItem>();
    public ICollection<ExpenseComment> Comments { get; set; } = new List<ExpenseComment>();
    public ICollection<ExpenseRevision> History { get; set; } = new List<ExpenseRevision>();
}
