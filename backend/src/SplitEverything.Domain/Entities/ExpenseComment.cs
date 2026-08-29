namespace SplitEverything.Domain.Entities;

public class ExpenseComment : SyncableEntity
{
    public Guid ExpenseId { get; set; }
    public Expense? Expense { get; set; }
    public Guid GroupId { get; set; }

    public Guid AuthorMemberId { get; set; }
    public GroupMember? AuthorMember { get; set; }

    /// <summary>Null for a top-level comment; set for a reply (single level of threading).</summary>
    public Guid? ParentCommentId { get; set; }
    public ExpenseComment? ParentComment { get; set; }

    public string Body { get; set; } = string.Empty;
    public DateTimeOffset? EditedAt { get; set; }

    public ICollection<ExpenseComment> Replies { get; set; } = new List<ExpenseComment>();
}
