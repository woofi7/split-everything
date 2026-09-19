using SplitEverything.Application.Contracts.Import;

namespace SplitEverything.Application.Services;

public interface IImportService
{
    Task<CsvAnalysisResult> AnalyzeCsvAsync(Guid userId, Stream csv, string? fileName, CancellationToken ct = default);

    Task<CsvPreviewResult> PreviewCsvAsync(Guid userId, Stream csv, CsvPreviewRequest request, CancellationToken ct = default);

    Task<ImportCommitResult> CommitCsvAsync(Guid userId, Stream csv, CsvCommitRequest request, CancellationToken ct = default);

    Task<ImportCommitResult> CommitStatementAsync(Guid userId, StatementCommitRequest request, CancellationToken ct = default);

    Task<DuplicateCheckResult> CheckDuplicatesAsync(Guid userId, DuplicateCheckRequest request, CancellationToken ct = default);

    Task<SplitSuggestionResult> GetSplitSuggestionsAsync(Guid userId, SplitSuggestionRequest request, CancellationToken ct = default);

    Task RollbackBatchAsync(Guid userId, Guid batchId, CancellationToken ct = default);
}
