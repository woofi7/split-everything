using SplitEverything.Domain.Common;

namespace SplitEverything.Application.Contracts.Admin;

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

public sealed record AdminGroupDetailDto(
    AdminGroupDto Group,
    IReadOnlyList<AdminMemberDto> Members,
    IReadOnlyList<AdminExpenseDto> RecentExpenses);
