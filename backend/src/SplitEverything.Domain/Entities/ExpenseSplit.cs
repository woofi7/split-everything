namespace SplitEverything.Domain.Entities;

public class ExpenseSplit : SyncableEntity
{
    public Guid ExpenseId { get; set; }
    public Expense? Expense { get; set; }

    public Guid MemberId { get; set; }
    public GroupMember? Member { get; set; }

    public decimal Amount { get; set; }

    public decimal AmountInBaseCurrency { get; set; }

    public decimal? InputValue { get; set; }

    public Guid GroupId { get; set; }
}
