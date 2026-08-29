namespace SplitEverything.Domain.Entities;

/// <summary>
/// Metadata for a stored receipt image. The bytes live behind IReceiptStorage, so
/// the row holds a storage key rather than a path.
/// </summary>
public class Receipt
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string StorageKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = "image/jpeg";
    public long SizeBytes { get; set; }
    public string? OriginalFileName { get; set; }

    /// <summary>SHA-256 of the bytes, used to avoid storing the same photo twice.</summary>
    public string ContentHash { get; set; } = string.Empty;

    public Guid UploadedByUserId { get; set; }
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;

    public int? Width { get; set; }
    public int? Height { get; set; }
}
