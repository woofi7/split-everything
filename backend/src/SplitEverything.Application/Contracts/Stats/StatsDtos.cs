namespace SplitEverything.Application.Contracts.Stats;

/// <summary>One person's share of a bucket, so a bar can be stacked by who paid.</summary>
public sealed record SpendPointMemberDto(Guid MemberId, string MemberName, decimal Amount);

public sealed record SpendPointDto(
    DateOnly Bucket,
    decimal Amount,
    int ExpenseCount,
    // Who paid within this bucket, largest first, and always summing to Amount.
    // Anyone who paid nothing in it is left out: a zero-height segment is not
    // information.
    IReadOnlyList<SpendPointMemberDto> ByMember);

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
    IReadOnlyList<MemberSpendDto> ByMember,
    IReadOnlyList<DebtTrendPointDto> DebtTrends);
