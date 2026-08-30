using SplitEverything.Domain.Common;

namespace SplitEverything.Application.Contracts.Expenses;

public sealed record SplitInputDto(Guid MemberId, decimal? Value);

public sealed record ExpenseItemShareDto(Guid MemberId);

public sealed record ExpenseItemDto(
    Guid? Id, string Description, decimal Amount, int Quantity, int SortOrder, IReadOnlyList<Guid> MemberIds);

public sealed record CreateExpenseRequest(
    Guid GroupId,
    Guid PaidByMemberId,
    string Description,
    decimal Amount,
    string Currency,
    DateTimeOffset SpentAt,
    SplitType SplitType,
    IReadOnlyList<SplitInputDto> Splits,
    IReadOnlyList<ExpenseItemDto>? Items,
    Guid? ReceiptId,
    string? Notes,
    // Client-generated id, so an offline create is idempotent on replay.
Guid? ClientId,
    string? ImportFingerprint,
    Guid? ImportBatchId);

public sealed record UpdateExpenseRequest(
    Guid? PaidByMemberId,
    string? Description,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? SpentAt,
    SplitType? SplitType,
    IReadOnlyList<SplitInputDto>? Splits,
    IReadOnlyList<ExpenseItemDto>? Items,
    Guid? ReceiptId,
    string? Notes,
    // Clock the client based the edit on; concurrent edits are flagged, never overwritten.
IReadOnlyDictionary<string, long>? BaseVectorClock);

public sealed record ExpenseSplitDto(Guid MemberId, string MemberName, decimal Amount, decimal AmountInBaseCurrency, decimal? InputValue);

public sealed record ExpenseDto(
    Guid Id,
    Guid GroupId,
    Guid PaidByMemberId,
    string PaidByName,
    string Description,
    decimal Amount,
    string Currency,
    decimal AmountInBaseCurrency,
    decimal ExchangeRate,
    DateTimeOffset SpentAt,
    SplitType SplitType,
    Guid? ReceiptId,
    string? Notes,
    int Revision,
    Guid? RecurringExpenseId,
    Guid? OriginGroupId,
    IReadOnlyList<ExpenseSplitDto> Splits,
    IReadOnlyList<ExpenseItemDto> Items,
    int CommentCount,
    IReadOnlyDictionary<string, long> VectorClock,
    long ServerSeq,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ExpenseRevisionDto(
    Guid Id, int Revision, Guid? EditedByUserId, string? EditedByName,
    DateTimeOffset EditedAt, string? ChangeSummary, Guid GroupId, string SnapshotJson);

public sealed record CreateCommentRequest(Guid ExpenseId, string Body, Guid? ParentCommentId, Guid? ClientId);

public sealed record CommentDto(
    Guid Id, Guid ExpenseId, Guid AuthorMemberId, string AuthorName, string? AuthorAvatarUrl,
    Guid? ParentCommentId, string Body, DateTimeOffset CreatedAt, DateTimeOffset? EditedAt,
    IReadOnlyList<CommentDto> Replies);

public sealed record ExpenseQuery(
    Guid? GroupId = null,
    Guid? MemberId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 50);

public sealed record CreateRecurringExpenseRequest(
    Guid GroupId,
    Guid PaidByMemberId,
    string Description,
    decimal Amount,
    string Currency,
    SplitType SplitType,
    IReadOnlyList<SplitInputDto> Splits,
    RecurrenceUnit Unit,
    int Interval,
    int? DayOfMonth,
    DayOfWeek? DayOfWeek,
    DateTimeOffset StartsOn,
    DateTimeOffset? EndsOn,
    int? MaxOccurrences);

public sealed record RecurringExpenseDto(
    Guid Id, Guid GroupId, string Description, decimal Amount, string Currency,
    RecurrenceUnit Unit, int Interval, DateTimeOffset NextRunAt, DateTimeOffset? LastRunAt,
    int OccurrenceCount, int? MaxOccurrences, bool IsPaused);
