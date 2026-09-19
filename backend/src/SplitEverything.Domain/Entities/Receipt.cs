namespace SplitEverything.Domain.Entities;

public class Receipt
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string StorageKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = "image/jpeg";
    public long SizeBytes { get; set; }
    public string? OriginalFileName { get; set; }

    public string ContentHash { get; set; } = string.Empty;

    public Guid UploadedByUserId { get; set; }
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;

    public int? Width { get; set; }
    public int? Height { get; set; }
}
