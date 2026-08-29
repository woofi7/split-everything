namespace SplitEverything.Domain.Entities;

public class ExpenseSplit : SyncableEntity
{
    public Guid ExpenseId { get; set; }
    public Expense? Expense { get; set; }

    public Guid MemberId { get; set; }
    public GroupMember? Member { get; set; }

    /// <summary>Settled share owed by this member, in the expense currency.</summary>
    public decimal Amount { get; set; }

    /// <summary>Share in the group base currency, using the expense's frozen rate.</summary>
    public decimal AmountInBaseCurrency { get; set; }

    /// <summary>
    /// The raw input behind <see cref="Amount"/>: a percentage, a share count, or an
    /// exact amount, depending on the expense's split type. Kept so re-editing an
    /// expense shows what the user actually typed rather than a rounded result.
    /// </summary>
    public decimal? InputValue { get; set; }

    /// <summary>Denormalised group id so the sync log can filter splits by group without a join.</summary>
    public Guid GroupId { get; set; }
}
