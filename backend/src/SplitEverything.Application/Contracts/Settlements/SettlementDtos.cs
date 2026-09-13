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

/// <summary>
/// What two people owe each other across every group they share, and what
/// cancelling the opposing halves would do.
///
/// A debt in one group and the opposite debt in another are the same two people,
/// so paying both in full is two transfers where none is needed. This is the
/// picture that says so: every shared group with something outstanding between
/// them, the pairs that cancel, and what is left when they have.
/// </summary>
public sealed record CrossGroupBalanceDto(
    Guid WithUserId,
    string WithName,
    IReadOnlyList<CrossGroupGroupDto> Groups,
    IReadOnlyList<PlannedOffsetDto> Offsets,
    IReadOnlyList<CrossGroupRemainderDto> Remaining);

/// <summary>
/// One shared group. Net is from the caller's side: positive means the other
/// person owes them that much here.
/// </summary>
public sealed record CrossGroupGroupDto(
    Guid GroupId, string GroupName, string Currency, decimal Net, bool CanSettle);

/// <summary>
/// One cancelling pair: this much of what is owed in one group is met by what is
/// owed the other way in another, so a settlement goes in each and no money moves.
/// </summary>
public sealed record PlannedOffsetDto(
    Guid OwedGroupId, string OwedGroupName,
    Guid OwingGroupId, string OwingGroupName,
    decimal Amount, string Currency);

/// <summary>What is still outstanding once the pairs have cancelled, per currency.</summary>
public sealed record CrossGroupRemainderDto(
    string Currency, decimal Net, Guid? GroupId, string? GroupName);

public sealed record OffsetAcrossGroupsRequest(Guid WithUserId, string? Note);

public sealed record OffsetAcrossGroupsResult(
    IReadOnlyList<PlannedOffsetDto> Applied,
    IReadOnlyList<CrossGroupRemainderDto> Remaining,
    int SettlementsRecorded);
