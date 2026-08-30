using Microsoft.AspNetCore.Mvc;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Import;
using SplitEverything.Application.Services;

namespace SplitEverything.Api.Controllers;

/// <summary>
/// Two importers, deliberately asymmetric.
///
/// A Settle Up CSV is uploaded and parsed here. A bank statement is not: it is
/// parsed in the browser and only the confirmed rows are posted, so there is no
/// endpoint on this controller that accepts a statement file at all.
/// </summary>
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

    /// <summary>Commits the rows a user confirmed in the client-side statement wizard.</summary>
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

    /// <summary>
    /// The mapping travels as a JSON form field alongside the file, since a
    /// multipart request cannot carry a JSON body as well.
    /// </summary>
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
