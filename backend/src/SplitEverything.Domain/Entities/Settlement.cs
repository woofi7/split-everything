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

    /// <summary>
    /// The settlement in another group this one cancels out, or null for the
    /// ordinary case of money actually changing hands.
    ///
    /// Two people who share two groups can owe each other in both directions at
    /// once: a thousand one way in the flat, nine hundred the other way on a trip.
    /// Offsetting those writes a pair of settlements, one in each group, for the
    /// smaller of the two amounts and in opposite directions. No money moves. The
    /// pair has to be recorded as a pair, or a group's ledger would show a payment
    /// nobody made with nothing to say where it came from - and deleting one half
    /// on its own would leave the two groups disagreeing by the amount.
    /// </summary>
    public Guid? OffsetSettlementId { get; set; }

    public Guid OriginLineageId { get; set; }
}
