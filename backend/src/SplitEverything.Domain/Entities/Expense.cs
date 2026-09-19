using SplitEverything.Domain.Common;

namespace SplitEverything.Domain.Entities;

public class Expense : SyncableEntity
{
    public Guid GroupId { get; set; }
    public Group? Group { get; set; }

    public Guid PaidByMemberId { get; set; }
    public GroupMember? PaidByMember { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "CAD";

    public decimal AmountInBaseCurrency { get; set; }
    public decimal ExchangeRate { get; set; } = 1m;
    public DateTimeOffset? ExchangeRateAsOf { get; set; }

    public DateTimeOffset SpentAt { get; set; } = DateTimeOffset.UtcNow;

    public SplitType SplitType { get; set; } = SplitType.Equal;

    public Guid? ReceiptId { get; set; }
    public Receipt? Receipt { get; set; }

    public string? Notes { get; set; }

    public string? CategoryKey { get; set; }

    public Guid? RecurringExpenseId { get; set; }
    public RecurringExpense? RecurringExpense { get; set; }

    public Guid? OriginGroupId { get; set; }

    public Guid OriginLineageId { get; set; }

    public int Revision { get; set; } = 1;

    public string? ImportFingerprint { get; set; }
    public Guid? ImportBatchId { get; set; }

    public ICollection<ExpensePayer> Payers { get; set; } = new List<ExpensePayer>();

    public ICollection<ExpenseSplit> Splits { get; set; } = new List<ExpenseSplit>();
    public ICollection<ExpenseItem> Items { get; set; } = new List<ExpenseItem>();
    public ICollection<ExpenseComment> Comments { get; set; } = new List<ExpenseComment>();
    public ICollection<ExpenseRevision> History { get; set; } = new List<ExpenseRevision>();
}
