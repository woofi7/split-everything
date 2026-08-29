using SplitEverything.Application.Contracts.Import;

namespace SplitEverything.Application.Services;

public interface IImportService
{
    /// <summary>Sniffs delimiter, headers and member names from an uploaded Settle Up CSV.</summary>
    Task<CsvAnalysisResult> AnalyzeCsvAsync(Guid userId, Stream csv, string? fileName, CancellationToken ct = default);

    /// <summary>Parses with the confirmed mapping and flags unparseable and duplicate rows.</summary>
    Task<CsvPreviewResult> PreviewCsvAsync(Guid userId, Stream csv, CsvPreviewRequest request, CancellationToken ct = default);

    Task<ImportCommitResult> CommitCsvAsync(Guid userId, Stream csv, CsvCommitRequest request, CancellationToken ct = default);

    /// <summary>Commits rows the user confirmed in the client-side statement wizard.</summary>
    Task<ImportCommitResult> CommitStatementAsync(Guid userId, StatementCommitRequest request, CancellationToken ct = default);

    Task<DuplicateCheckResult> CheckDuplicatesAsync(Guid userId, DuplicateCheckRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<CategoryRuleDto>> GetCategoryRulesAsync(Guid userId, CancellationToken ct = default);
    Task<CategoryRuleDto> UpsertCategoryRuleAsync(Guid userId, UpsertCategoryRuleRequest request, CancellationToken ct = default);
    Task DeleteCategoryRuleAsync(Guid userId, Guid ruleId, CancellationToken ct = default);

    /// <summary>Prior splits per merchant, so the client can suggest what was used last time.</summary>
    Task<SplitSuggestionResult> GetSplitSuggestionsAsync(Guid userId, SplitSuggestionRequest request, CancellationToken ct = default);

    Task RollbackBatchAsync(Guid userId, Guid batchId, CancellationToken ct = default);
}
