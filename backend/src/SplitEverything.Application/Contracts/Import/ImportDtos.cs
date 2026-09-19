using SplitEverything.Domain.Common;

namespace SplitEverything.Application.Contracts.Import;

public sealed record CsvAnalysisResult(
    Guid AnalysisId,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> SampleRows,
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
    int? PaidByColumn,
    IReadOnlyDictionary<int, string>? ParticipantColumns,
    string? DateFormat,
    string? DecimalSeparator,
    int? ParticipantsColumn = null,
    int? SplitAmountsColumn = null,
    int? TypeColumn = null);

public sealed record CsvPreviewRequest(
    Guid? GroupId,
    CsvColumnMapping Mapping,
    IReadOnlyDictionary<string, Guid?> MemberNameMapping,
    string? FallbackCurrency,
    IReadOnlyDictionary<string, Guid>? MemberUserMapping = null);

public sealed record ImportPayerShare(string Name, Guid? MemberId, decimal Amount);

public static class ImportRowNames
{
    public static IEnumerable<string> PeopleIn(ParsedExpenseRow row)
    {
        foreach (var participant in row.ParticipantNames) yield return participant;

        if (row.Payers is { Count: > 0 })
        {
            foreach (var payer in row.Payers) yield return payer.Name;
            yield break;
        }

        if (row.PaidByName is not null) yield return row.PaidByName;
    }
}

public sealed record ParsedExpenseRow(
    int RowNumber,
    DateTimeOffset? SpentAt,
    string Description,
    decimal? Amount,
    string? Currency,
    string? PaidByName,
    Guid? PaidByMemberId,
    IReadOnlyList<string> ParticipantNames,
    IReadOnlyList<Guid> ParticipantMemberIds,
    string Fingerprint,
    bool IsDuplicate,
    Guid? DuplicateOfExpenseId,
    IReadOnlyList<string> Problems,
    IReadOnlyList<decimal>? SplitAmounts = null,
    bool IsSettlement = false,
    IReadOnlyList<ImportPayerShare>? Payers = null)
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
    Guid? GroupId,
    string? NewGroupName,
    CsvColumnMapping Mapping,
    IReadOnlyDictionary<string, Guid?> MemberNameMapping,
    IReadOnlyList<int> SkipRowNumbers,
    bool CreateMissingMembers,
    bool SkipDuplicates,
    string? FallbackCurrency,
    string? SourceLabel,
    IReadOnlyDictionary<string, Guid>? MemberUserMapping = null);

public sealed record ImportCommitResult(
    Guid ImportBatchId,
    Guid GroupId,
    int CreatedExpenses,
    int CreatedSettlements,
    int SkippedRows,
    IReadOnlyList<Guid> CreatedMemberIds,
    IReadOnlyList<string> Warnings);

public sealed record ConfirmedStatementRow(
    Guid GroupId,
    Guid PaidByMemberId,
    string Description,
    decimal Amount,
    string Currency,
    DateTimeOffset SpentAt,
    SplitType SplitType,
    IReadOnlyList<Expenses.SplitInputDto> Splits,
    string Fingerprint,
    string? Notes,
    string? CategoryKey = null);

public sealed record StatementCommitRequest(
    IReadOnlyList<ConfirmedStatementRow> Rows,
    bool SkipDuplicates,
    string? SourceLabel);

public sealed record DuplicateCheckRequest(IReadOnlyList<string> Fingerprints, Guid? GroupId);

public sealed record DuplicateMatchDto(string Fingerprint, Guid ExpenseId, Guid GroupId, string GroupName, string Description, decimal Amount, DateTimeOffset SpentAt);

public sealed record DuplicateCheckResult(IReadOnlyList<DuplicateMatchDto> Matches);

public sealed record SplitSuggestionDto(
    string NormalizedMerchant,
    Guid GroupId,
    string GroupName,
    SplitType SplitType,
    IReadOnlyList<Expenses.SplitInputDto> Splits,
    Guid PaidByMemberId,
    int TimesUsed,
    DateTimeOffset LastUsedAt);

public sealed record SplitSuggestionRequest(IReadOnlyList<string> Merchants);

public sealed record SplitSuggestionResult(IReadOnlyList<SplitSuggestionDto> Suggestions);
