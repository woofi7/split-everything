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
    int? PaidByColumn,
    // Column index -> member name, for exports with one column per participant.
    IReadOnlyDictionary<int, string>? ParticipantColumns,
    string? DateFormat,
    string? DecimalSeparator,
    // The single "for whom" cell, several names in one column. Without it the
    // participants were guessed to sit beside the payer, which in a real export is
    // the amount.
    int? ParticipantsColumn = null,
    // Per-person amounts, in the same order as the participants. Present in real
    // exports, and the only way an uneven split survives the round trip.
    int? SplitAmountsColumn = null,
    // "expense" or "transfer". A transfer is a settlement, and booking it as an
    // expense moves every balance in the group by the wrong amount twice over.
    int? TypeColumn = null);

public sealed record CsvPreviewRequest(
    // Null while the wizard is still importing into a group that does not exist
    // yet: there are no members to match against and no history to duplicate.
    Guid? GroupId,
    CsvColumnMapping Mapping,
    // Settle Up exports carry display names, not emails; the user maps them to members.
    IReadOnlyDictionary<string, Guid?> MemberNameMapping,
    string? FallbackCurrency,
    // Names matched to an account rather than to a member of this group. The
    // account may not be in the group yet, and the group may not exist yet, which
    // is the normal case for an export: it is somebody else's group history and
    // the people in it have their own accounts here.
    IReadOnlyDictionary<string, Guid>? MemberUserMapping = null);

/// <summary>
/// One payer of a shared payment, and what they put in.
///
/// Settle Up lets several people pay for the same thing; an expense here has one
/// payer. So a row with two payers becomes two expenses, and this is how the row
/// carries them as far as the commit.
/// </summary>
public sealed record ImportPayerShare(string Name, Guid? MemberId, decimal Amount);

public static class ImportRowNames
{
    /// <summary>
    /// Every person a row names, one name each.
    ///
    /// PaidByName is for reading - "Emma, Nicolas" when two people paid - so it is
    /// never a name to look somebody up by or create somebody from. This walks the
    /// payers instead, which are one entry per person.
    /// </summary>
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
    // Per-person amounts from the export, in participant order. Empty when the
    // export does not carry them, in which case the split is computed.
    IReadOnlyList<decimal>? SplitAmounts = null,
    // A settlement rather than an expense: someone paying down what they owed.
    bool IsSettlement = false,
    // Set only when the row names more than one payer, in which case Amount is
    // their total and each of these becomes an expense of its own.
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
    // Null to create a group for this import, named by NewGroupName. An export is
    // one group's history, so a group that does not exist here yet is the ordinary
    // case rather than the exception.
    Guid? GroupId,
    string? NewGroupName,
    CsvColumnMapping Mapping,
    IReadOnlyDictionary<string, Guid?> MemberNameMapping,
    // Rows the user unticked in the preview.
    IReadOnlyList<int> SkipRowNumbers,
    // Create group placeholders for names with no match, instead of failing.
    bool CreateMissingMembers,
    bool SkipDuplicates,
    string? FallbackCurrency,
    string? SourceLabel,
    // Names matched to an account rather than to a member of this group. Each one
    // becomes a member of the group as the import runs, so an export can be bound
    // to the people who are already here rather than to placeholders wearing their
    // names.
    IReadOnlyDictionary<string, Guid>? MemberUserMapping = null);

public sealed record ImportCommitResult(
    Guid ImportBatchId,
    Guid GroupId,
    int CreatedExpenses,
    int CreatedSettlements,
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
    int TimesUsed,
    DateTimeOffset LastUsedAt);

public sealed record SplitSuggestionRequest(IReadOnlyList<string> Merchants);

public sealed record SplitSuggestionResult(IReadOnlyList<SplitSuggestionDto> Suggestions);
