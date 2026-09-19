using Microsoft.AspNetCore.Mvc;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Import;
using SplitEverything.Application.Services;

namespace SplitEverything.Api.Controllers;

public sealed class ImportController(
    ICurrentUser currentUser,
    IImportService imports) : ApiControllerBase(currentUser)
{
    private const long MaxCsvBytes = 8 * 1024 * 1024;

    [HttpPost("csv/analyze")]
    [RequestSizeLimit(MaxCsvBytes)]
    public async Task<ActionResult<CsvAnalysisResult>> Analyze(IFormFile file, CancellationToken ct)
    {
        await using var stream = OpenCsv(file);
        return Ok(await imports.AnalyzeCsvAsync(UserId, stream, file.FileName, ct));
    }

    [HttpPost("csv/preview")]
    [RequestSizeLimit(MaxCsvBytes)]
    public async Task<ActionResult<CsvPreviewResult>> Preview(
        IFormFile file, [FromForm] string request, CancellationToken ct)
    {
        await using var stream = OpenCsv(file);
        return Ok(await imports.PreviewCsvAsync(UserId, stream, Deserialize<CsvPreviewRequest>(request), ct));
    }

    [HttpPost("csv/commit")]
    [RequestSizeLimit(MaxCsvBytes)]
    public async Task<ActionResult<ImportCommitResult>> Commit(
        IFormFile file, [FromForm] string request, CancellationToken ct)
    {
        await using var stream = OpenCsv(file);
        var commit = Deserialize<CsvCommitRequest>(request);
        return Ok(await imports.CommitCsvAsync(UserId,
            stream, commit with { SourceLabel = commit.SourceLabel ?? file.FileName }, ct));
    }

    [HttpPost("statement/commit")]
    public async Task<ActionResult<ImportCommitResult>> CommitStatement(
        StatementCommitRequest request, CancellationToken ct)
        => Ok(await imports.CommitStatementAsync(UserId, request, ct));

    [HttpPost("duplicates")]
    public async Task<ActionResult<DuplicateCheckResult>> CheckDuplicates(
        DuplicateCheckRequest request, CancellationToken ct)
        => Ok(await imports.CheckDuplicatesAsync(UserId, request, ct));

    [HttpPost("split-suggestions")]
    public async Task<ActionResult<SplitSuggestionResult>> SplitSuggestions(
        SplitSuggestionRequest request, CancellationToken ct)
        => Ok(await imports.GetSplitSuggestionsAsync(UserId, request, ct));

    [HttpPost("batches/{batchId:guid}/rollback")]
    public async Task<IActionResult> Rollback(Guid batchId, CancellationToken ct)
    {
        await imports.RollbackBatchAsync(UserId, batchId, ct);
        return NoContent();
    }

    private static Stream OpenCsv(IFormFile? file)
    {
        if (file is null || file.Length == 0)
            throw new ValidationException("Attach a CSV file.");
        if (file.Length > MaxCsvBytes)
            throw new ValidationException($"That file is larger than {MaxCsvBytes / (1024 * 1024)} MB.");

        return file.OpenReadStream();
    }

    private static T Deserialize<T>(string json)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<T>(json, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new ValidationException("The import request was empty.");
        }
        catch (System.Text.Json.JsonException)
        {
            throw new ValidationException("The import request could not be read.");
        }
    }
}
