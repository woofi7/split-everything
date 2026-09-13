using SplitEverything.Domain.Common;

namespace SplitEverything.Application.Contracts.Admin;

/// <summary>
/// A group as the person who runs the server sees it: every group there is, not
/// only the ones they are in, with enough on each line to decide what to do about
/// it - how much is in it, who is in it, and whether it is still in use.
/// </summary>
public sealed record AdminGroupDto(
    Guid Id,
    string Name,
    string BaseCurrency,
    string? IconName,
    string ColorHex,
    bool IsArchived,
    DateTimeOffset? ArchivedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastActivityAt,
    string CreatedByName,
    int MemberCount,
    int ExpenseCount,
    decimal TotalSpend,
    /// <summary>Whether the administrator is a member of it themselves.</summary>
    bool IsMine);

public sealed record AdminMemberDto(
    Guid Id,
    string DisplayName,
    string? Email,
    GroupRole Role,
    MembershipStatus Status,
    bool IsPlaceholder);

public sealed record AdminExpenseDto(
    Guid Id,
    string Description,
    decimal Amount,
    string Currency,
    DateTimeOffset SpentAt,
    string PaidByName);

/// <summary>
/// One group, read-only, for a server administrator who is not in it. Enough to
/// answer "what is this and who is in it" without joining the group, which would
/// put a stranger in everybody's balances.
/// </summary>
public sealed record AdminGroupDetailDto(
    AdminGroupDto Group,
    IReadOnlyList<AdminMemberDto> Members,
    IReadOnlyList<AdminExpenseDto> RecentExpenses);
