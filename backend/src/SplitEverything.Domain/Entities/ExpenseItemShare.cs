namespace SplitEverything.Domain.Entities;

public class ExpenseItemShare
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ExpenseItemId { get; set; }
    public ExpenseItem? ExpenseItem { get; set; }
    public Guid MemberId { get; set; }
    public GroupMember? Member { get; set; }
}
