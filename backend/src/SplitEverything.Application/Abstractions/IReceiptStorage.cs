namespace SplitEverything.Application.Abstractions;

public sealed record StoredReceipt(string StorageKey, long SizeBytes, string ContentHash);

/// <summary>
/// Receipt blob store. Local disk today; the interface exists so moving to
/// S3/MinIO is a registration change rather than a rewrite, as the spec requires.
/// </summary>
public interface IReceiptStorage
{
    Task<StoredReceipt> SaveAsync(Stream content, string contentType, string? fileName, CancellationToken ct = default);
    Task<Stream?> OpenAsync(string storageKey, CancellationToken ct = default);
    Task DeleteAsync(string storageKey, CancellationToken ct = default);
    Task<bool> ExistsAsync(string storageKey, CancellationToken ct = default);
}
