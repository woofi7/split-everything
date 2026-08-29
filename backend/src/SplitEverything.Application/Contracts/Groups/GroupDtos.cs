using SplitEverything.Domain.Common;

namespace SplitEverything.Application.Contracts.Groups;

public sealed record CreateGroupRequest(
    string Name,
    string BaseCurrency,
    string? Description,
    string? IconName,
    string? ColorHex,
    IReadOnlyList<string>? PlaceholderMemberNames);

public sealed record UpdateGroupRequest(
    string? Name, string? Description, string? IconName, string? ColorHex, string? BaseCurrency);

public sealed record GroupMemberDto(
    Guid Id,
    Guid? UserId,
    string DisplayName,
    string? AvatarUrl,
    GroupRole Role,
    MembershipStatus Status,
    bool IsPlaceholder,
    decimal NetBalance);

public sealed record GroupDto(
    Guid Id,
    string Name,
    string? Description,
    string BaseCurrency,
    string? IconName,
    string ColorHex,
    bool IsArchived,
    long SequenceCounter,
    Guid LineageId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<GroupMemberDto> Members,
    decimal MyNetBalance,
    decimal TotalSpend,
    int ExpenseCount);

public sealed record GroupSummaryDto(
    Guid Id, string Name, string BaseCurrency, string? IconName, string ColorHex,
    bool IsArchived, decimal MyNetBalance, int MemberCount, DateTimeOffset? LastActivityAt);

public sealed record AddPlaceholderMemberRequest(string DisplayName);

public sealed record CreateInviteRequest(string? Email, Guid? ClaimsMemberId, int MaxUses, int ExpiresInHours);

public sealed record InviteDto(
    Guid Id, Guid GroupId, string GroupName, string Token, string Url,
    string? InvitedEmail, DateTimeOffset ExpiresAt, int MaxUses, int UseCount);

public sealed record InvitePreviewDto(Guid GroupId, string GroupName, string? IconName, string InvitedByName, int MemberCount, bool IsRedeemable);

public sealed record RedeemInviteResult(Guid GroupId, Guid MemberId, bool AlreadyMember);

/// <summary>Both groups keep their history; the target absorbs the source log.</summary>
public sealed record MergeGroupsRequest(Guid SourceGroupId, Guid TargetGroupId, IReadOnlyDictionary<Guid, Guid>? MemberMapping, string? Note);

public sealed record MergeGroupsResult(Guid TargetGroupId, Guid ArchivedSourceGroupId, int MovedExpenses, int MovedSettlements, int MovedLogEntries, Guid LineageLinkId);

/// <summary>Moves the listed expenses and settlements into a brand new group.</summary>
public sealed record SplitGroupRequest(
    Guid SourceGroupId,
    string NewGroupName,
    IReadOnlyList<Guid> ExpenseIds,
    IReadOnlyList<Guid>? SettlementIds,
    IReadOnlyList<Guid>? MemberIds,
    string? Note);

public sealed record SplitGroupResult(Guid SourceGroupId, Guid NewGroupId, int MovedExpenses, int MovedSettlements, int MovedLogEntries, Guid LineageLinkId);

public sealed record TransferExpenseRequest(Guid ExpenseId, Guid TargetGroupId, IReadOnlyDictionary<Guid, Guid>? MemberMapping);

public sealed record TransferExpenseResult(Guid ExpenseId, Guid FromGroupId, Guid ToGroupId, int MovedRevisions, int MovedComments, int MovedLogEntries);

public sealed record GroupLineageDto(
    Guid Id, GroupLineageKind Kind, Guid SourceGroupId, string? SourceGroupName,
    Guid TargetGroupId, string? TargetGroupName, DateTimeOffset OccurredAt, string? Note);
