namespace SplitEverything.Domain.Entities;

/// <summary>
/// A real transfer of money between two members, which cancels out debt.
/// </summary>
public class Settlement : SyncableEntity
{
    public Guid GroupId { get; set; }
    public Group? Group { get; set; }

    public Guid FromMemberId { get; set; }
    public GroupMember? FromMember { get; set; }

    public Guid ToMemberId { get; set; }
    public GroupMember? ToMember { get; set; }

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "CAD";
    public decimal AmountInBaseCurrency { get; set; }
    public decimal ExchangeRate { get; set; } = 1m;

    public DateTimeOffset SettledAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Note { get; set; }

    public Guid? ReceiptId { get; set; }
    public Receipt? Receipt { get; set; }

    public Guid OriginLineageId { get; set; }
}
