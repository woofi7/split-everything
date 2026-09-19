namespace SplitEverything.Domain.Entities;

public class ExpenseItem : SyncableEntity
{
    public Guid ExpenseId { get; set; }
    public Expense? Expense { get; set; }
    public Guid GroupId { get; set; }

    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int Quantity { get; set; } = 1;
    public int SortOrder { get; set; }

    public ICollection<ExpenseItemShare> Shares { get; set; } = new List<ExpenseItemShare>();
}
