using SplitEverything.Domain.Common;

namespace SplitEverything.Application.Contracts.Import;

/// <summary>
/// Result of parsing an uploaded Settle Up CSV export. The layout varies by app
/// version and locale, so we report what we detected and let the user remap.
/// </summary>
public sealed record CsvAnalysisResult(
    Guid AnalysisId,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> SampleRows,
    // Header index guessed for each known field, -1 when we could not find it.
    IReadOnlyDictionary<string, int> SuggestedMapping,
    IReadOnlyList<string> DetectedMemberNames,
    string DetectedDelimiter,
    string? DetectedCurrency,
    int RowCount);

public sealed record CsvColumnMapping(
    int DateColumn,
    int DescriptionColumn,
    int AmountColumn,
    int? CurrencyColumn,
    int? CategoryColumn,
    int? PaidByColumn,
    // Column index -> member name, for exports with one column per participant.
    IReadOnlyDictionary<int, string>? ParticipantColumns,
    string? DateFormat,
    string? DecimalSeparator);

public sealed record CsvPreviewRequest(
    Guid GroupId,
    CsvColumnMapping Mapping,
    // Settle Up exports carry display names, not emails; the user maps them to members.
    IReadOnlyDictionary<string, Guid?> MemberNameMapping,
    string? FallbackCurrency);

public sealed record ParsedExpenseRow(
    int RowNumber,
    DateTimeOffset? SpentAt,
    string Description,
    decimal? Amount,
    string? Currency,
    string? CategoryName,
    string? PaidByName,
    Guid? PaidByMemberId,
    IReadOnlyList<string> ParticipantNames,
    IReadOnlyList<Guid> ParticipantMemberIds,
    string Fingerprint,
    bool IsDuplicate,
    Guid? DuplicateOfExpenseId,
    IReadOnlyList<string> Problems)
{
    public bool IsCommittable => Problems.Count == 0 && SpentAt is not null && Amount is not null;
}

public sealed record CsvPreviewResult(
    Guid AnalysisId,
    IReadOnlyList<ParsedExpenseRow> Rows,
    int CommittableCount,
    int ProblemCount,
    int DuplicateCount,
    IReadOnlyList<string> UnmappedMemberNames);

public sealed record CsvCommitRequest(
    Guid AnalysisId,
    Guid GroupId,
    CsvColumnMapping Mapping,
    IReadOnlyDictionary<string, Guid?> MemberNameMapping,
    // Rows the user unticked in the preview.
    IReadOnlyList<int> SkipRowNumbers,
    // Create group placeholders for names with no match, instead of failing.
    bool CreateMissingMembers,
    bool SkipDuplicates,
    string? FallbackCurrency,
    string? SourceLabel);

public sealed record ImportCommitResult(
    Guid ImportBatchId,
    Guid GroupId,
    int CreatedExpenses,
    int SkippedRows,
    IReadOnlyList<Guid> CreatedMemberIds,
    IReadOnlyList<string> Warnings);

/// <summary>
/// A statement row the user confirmed in the client-side wizard.
///
/// The statement file itself never reaches the API: parsing, OCR and
/// categorisation all happen in the browser, and only these confirmed records
/// are posted on commit.
/// </summary>
public sealed record ConfirmedStatementRow(
    Guid GroupId,
    Guid PaidByMemberId,
    string Description,
    decimal Amount,
    string Currency,
    DateTimeOffset SpentAt,
    Guid? CategoryId,
    SplitType SplitType,
    IReadOnlyList<Expenses.SplitInputDto> Splits,
    string Fingerprint,
    string? Notes);

public sealed record StatementCommitRequest(
    IReadOnlyList<ConfirmedStatementRow> Rows,
    bool SkipDuplicates,
    // File name only, for the user's own reference; never the file.
    string? SourceLabel);

public sealed record DuplicateCheckRequest(IReadOnlyList<string> Fingerprints, Guid? GroupId);

public sealed record DuplicateMatchDto(string Fingerprint, Guid ExpenseId, Guid GroupId, string GroupName, string Description, decimal Amount, DateTimeOffset SpentAt);

public sealed record DuplicateCheckResult(IReadOnlyList<DuplicateMatchDto> Matches);

public sealed record CategoryRuleDto(Guid Id, string Keyword, Guid CategoryId, string CategoryKey, Guid? SuggestedGroupId, int Weight, int HitCount, bool IsEnabled, bool IsBuiltIn);

public sealed record UpsertCategoryRuleRequest(Guid? Id, string Keyword, Guid CategoryId, Guid? SuggestedGroupId, bool IsEnabled);

/// <summary>
/// Prior splits for a merchant, so the client can suggest the same split it used
/// last time. Returns only aggregate history, never statement content.
/// </summary>
public sealed record SplitSuggestionDto(
    string NormalizedMerchant,
    Guid GroupId,
    string GroupName,
    SplitType SplitType,
    IReadOnlyList<Expenses.SplitInputDto> Splits,
    Guid PaidByMemberId,
    Guid? CategoryId,
    int TimesUsed,
    DateTimeOffset LastUsedAt);

public sealed record SplitSuggestionRequest(IReadOnlyList<string> Merchants);

public sealed record SplitSuggestionResult(IReadOnlyList<SplitSuggestionDto> Suggestions);
