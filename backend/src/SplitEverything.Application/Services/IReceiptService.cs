namespace SplitEverything.Application.Services;

public sealed record ReceiptDto(Guid Id, string ContentType, long SizeBytes, string? OriginalFileName, DateTimeOffset UploadedAt);

public sealed record ReceiptContent(Stream Content, string ContentType, string? FileName);

public interface IReceiptService
{
    Task<ReceiptDto> UploadAsync(Guid userId, Stream content, string contentType, string? fileName, CancellationToken ct = default);

    Task<ReceiptContent> DownloadAsync(Guid userId, Guid receiptId, CancellationToken ct = default);
}
