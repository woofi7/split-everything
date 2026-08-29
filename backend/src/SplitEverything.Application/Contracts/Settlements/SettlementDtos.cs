namespace SplitEverything.Application.Contracts.Settlements;

public sealed record CreateSettlementRequest(
    Guid GroupId,
    Guid FromMemberId,
    Guid ToMemberId,
    decimal Amount,
    string Currency,
    DateTimeOffset SettledAt,
    string? Note,
    Guid? ReceiptId,
    Guid? ClientId);

public sealed record SettlementDto(
    Guid Id, Guid GroupId,
    Guid FromMemberId, string FromMemberName,
    Guid ToMemberId, string ToMemberName,
    decimal Amount, string Currency, decimal AmountInBaseCurrency,
    DateTimeOffset SettledAt, string? Note, Guid? ReceiptId,
    IReadOnlyDictionary<string, long> VectorClock, long ServerSeq);

public sealed record SuggestedTransferDto(
    Guid FromMemberId, string FromMemberName,
    Guid ToMemberId, string ToMemberName,
    decimal Amount, string Currency);

public sealed record GroupBalanceDto(
    Guid GroupId,
    string BaseCurrency,
    IReadOnlyList<MemberBalanceDto> Balances,
    IReadOnlyList<SuggestedTransferDto> SimplifiedTransfers,
    IReadOnlyList<SuggestedTransferDto> PairwiseDebts);

public sealed record MemberBalanceDto(Guid MemberId, string MemberName, decimal Net);

public sealed record OverallBalanceDto(
    string Currency,
    decimal TotalOwedToMe,
    decimal TotalIOwe,
    decimal Net,
    IReadOnlyList<GroupNetDto> ByGroup);

public sealed record GroupNetDto(Guid GroupId, string GroupName, string Currency, decimal Net, decimal NetInUserCurrency);

public sealed record NudgeRequest(Guid GroupId, Guid MemberId, string? Message);
