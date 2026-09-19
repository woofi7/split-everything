namespace SplitEverything.Application.Contracts.Stats;

/// <summary>One person's share of a bucket, so a bar can be stacked by who paid.</summary>
public sealed record SpendPointMemberDto(Guid MemberId, string MemberName, decimal Amount);

/// <summary>
/// What one category came to inside one bucket, for the line drawn over the bars.
///
/// No expense count, unlike the breakdown for the whole window: this is read as a
/// height on a chart, and a count nobody can see on it is payload for nothing. A
/// daily chart of a quarter carries ninety of these lists.
/// </summary>
public sealed record SpendPointCategoryDto(string? Key, decimal Amount);

public sealed record SpendPointDto(
    DateOnly Bucket,
    decimal Amount,
    int ExpenseCount,
    // Who paid within this bucket, largest first, and always summing to Amount.
    // Anyone who paid nothing in it is left out: a zero-height segment is not
    // information.
    IReadOnlyList<SpendPointMemberDto> ByMember,
    // What it went on within this bucket, on the same terms: largest first,
    // summing to Amount, nothing in it for a category that spent nothing. This is
    // the other way of cutting the same money, and it is what lets one category be
    // traced across the chart instead of only totalled beside it.
    IReadOnlyList<SpendPointCategoryDto>? ByCategory = null);

public sealed record MemberSpendDto(Guid MemberId, string MemberName, decimal Paid, decimal Owed, decimal Net);

/// <summary>
/// What a category came to. Largest first, and the expenses nobody filed come back
/// under a null key rather than being dropped: a breakdown that quietly omits a
/// third of the spending is worse than one that admits to it.
/// </summary>
public sealed record CategorySpendDto(string? Key, decimal Amount, int ExpenseCount);

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
    IReadOnlyList<DebtTrendPointDto> DebtTrends,
    IReadOnlyList<CategorySpendDto>? ByCategory = null);
