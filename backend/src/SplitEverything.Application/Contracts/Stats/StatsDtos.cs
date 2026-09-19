namespace SplitEverything.Application.Contracts.Stats;

public sealed record SpendPointMemberDto(Guid MemberId, string MemberName, decimal Amount);

public sealed record SpendPointCategoryDto(string? Key, decimal Amount);

public sealed record SpendPointDto(
    DateOnly Bucket,
    decimal Amount,
    int ExpenseCount,
    IReadOnlyList<SpendPointMemberDto> ByMember,
    IReadOnlyList<SpendPointCategoryDto>? ByCategory = null);

public sealed record MemberSpendDto(Guid MemberId, string MemberName, decimal Paid, decimal Owed, decimal Net);

public sealed record CategorySpendDto(string? Key, decimal Amount, int ExpenseCount);

public sealed record DebtTrendPointDto(DateOnly Bucket, Guid MemberId, string MemberName, decimal Net);

public sealed record StatsQuery(
    Guid? GroupId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    string Granularity = "month",
    bool IncludeArchived = false);

public sealed record StatsDashboardDto(
    string Currency,
    decimal TotalSpend,
    decimal MyShare,
    decimal MyPaid,
    int ExpenseCount,
    DateTimeOffset? From,
    DateTimeOffset? To,
    IReadOnlyList<SpendPointDto> SpendOverTime,
    IReadOnlyList<MemberSpendDto> ByMember,
    IReadOnlyList<DebtTrendPointDto> DebtTrends,
    IReadOnlyList<CategorySpendDto>? ByCategory = null);
