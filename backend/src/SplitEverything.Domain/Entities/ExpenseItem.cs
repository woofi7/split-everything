namespace SplitEverything.Domain.Entities;

/// <summary>
/// One line of a receipt, for itemized splitting ("who had the appetizer").
/// </summary>
public class ExpenseItem : SyncableEntity
{
    public Guid ExpenseId { get; set; }
    public Expense? Expense { get; set; }
    public Guid GroupId { get; set; }

    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int Quantity { get; set; } = 1;
    public int SortOrder { get; set; }

    /// <summary>Members sharing this line, equally. Empty means it falls back to the whole group.</summary>
    public ICollection<ExpenseItemShare> Shares { get; set; } = new List<ExpenseItemShare>();
}
