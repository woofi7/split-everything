namespace SplitEverything.Application.Contracts.Stats;

public sealed record SpendPointDto(DateOnly Bucket, decimal Amount, int ExpenseCount);

public sealed record CategorySpendDto(Guid? CategoryId, string CategoryKey, string CategoryName, string Emoji, string ColorHex, decimal Amount, int ExpenseCount, decimal Share);

public sealed record MemberSpendDto(Guid MemberId, string MemberName, decimal Paid, decimal Owed, decimal Net);

public sealed record DebtTrendPointDto(DateOnly Bucket, Guid MemberId, string MemberName, decimal Net);

public sealed record StatsQuery(
    Guid? GroupId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    // "day", "week" or "month".
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
    IReadOnlyList<CategorySpendDto> ByCategory,
    IReadOnlyList<MemberSpendDto> ByMember,
    IReadOnlyList<DebtTrendPointDto> DebtTrends);
