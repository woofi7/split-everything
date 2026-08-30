namespace SplitEverything.Domain.Entities;

/// <summary>
/// One person's contribution to what an expense cost.
///
/// Most expenses have a single payer, and that row says so. Some are paid by more
/// than one person at once - two cards at the till, one person short of cash - and
/// what each of them put in is not the same question as what each of them owes.
/// Settle Up allows this, so an imported history contains it, and inventing a
/// single payer for a shared payment moves both balances by the wrong amount.
///
/// Every expense has at least one of these, and they sum to the expense amount.
/// <see cref="Expense.PaidByMemberId"/> is the largest of them.
/// </summary>
public class ExpensePayer : SyncableEntity
{
    public Guid ExpenseId { get; set; }
    public Expense? Expense { get; set; }

    public Guid MemberId { get; set; }
    public GroupMember? Member { get; set; }

    /// <summary>What this member put in, in the expense currency.</summary>
    public decimal Amount { get; set; }

    /// <summary>The same in the group base currency, using the expense's frozen rate.</summary>
    public decimal AmountInBaseCurrency { get; set; }

    /// <summary>Denormalised group id, so the sync log can filter by group without a join.</summary>
    public Guid GroupId { get; set; }
}
