using Microsoft.EntityFrameworkCore;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Common;
using SplitEverything.Application.Services;
using SplitEverything.Domain.Common;
using SplitEverything.Domain.Entities;
using SplitEverything.Infrastructure.Persistence;

namespace SplitEverything.Infrastructure.Services;

public sealed class ReceiptService(
    AppDbContext db,
    IReceiptStorage storage,
    IClock clock) : IReceiptService
{
    public const long MaxBytes = 12 * 1024 * 1024;

    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/jpg", "image/png", "image/webp", "image/heic", "image/heif", "application/pdf"
    };

    public async Task<ReceiptDto> UploadAsync(
        Guid userId, Stream content, string contentType, string? fileName, CancellationToken ct = default)
    {
        if (!AllowedTypes.Contains(contentType))
            throw new ValidationException("A receipt must be an image or a PDF.");

        if (content.CanSeek && content.Length > MaxBytes)
            throw new ValidationException($"A receipt must be smaller than {MaxBytes / (1024 * 1024)} MB.");

        var stored = await storage.SaveAsync(content, contentType, fileName, ct);

        if (stored.SizeBytes > MaxBytes)
        {
            // Non-seekable streams only reveal their size after the copy.
            await storage.DeleteAsync(stored.StorageKey, ct);
            throw new ValidationException($"A receipt must be smaller than {MaxBytes / (1024 * 1024)} MB.");
        }

        // The blob store keys by content hash, so the same photo uploaded twice is
        // one file and one row.
        var existing = await db.Receipts.FirstOrDefaultAsync(r => r.ContentHash == stored.ContentHash, ct);
        if (existing is not null) return Map(existing);

        var receipt = new Receipt
        {
            StorageKey = stored.StorageKey,
            ContentType = contentType,
            SizeBytes = stored.SizeBytes,
            OriginalFileName = fileName,
            ContentHash = stored.ContentHash,
            UploadedByUserId = userId,
            UploadedAt = clock.UtcNow
        };
        db.Receipts.Add(receipt);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        return Map(receipt);
    }

    public async Task<ReceiptContent> DownloadAsync(
        Guid userId, Guid receiptId, CancellationToken ct = default)
    {
        var receipt = await db.Receipts.FirstOrDefaultAsync(r => r.Id == receiptId, ct)
                      ?? throw new NotFoundException($"Receipt {receiptId}");

        if (!await CanReadAsync(userId, receipt, ct))
            throw new ForbiddenException("You do not have access to that receipt.");

        var content = await storage.OpenAsync(receipt.StorageKey, ct)
                      ?? throw new NotFoundException($"Receipt blob {receipt.StorageKey}");

        return new ReceiptContent(content, receipt.ContentType, receipt.OriginalFileName);
    }

    /// <summary>
    /// A receipt is readable by whoever uploaded it, and by any member of a group it
    /// is attached to. Attachment is the grant: an orphan receipt stays private to
    /// its uploader.
    /// </summary>
    private async Task<bool> CanReadAsync(Guid userId, Receipt receipt, CancellationToken ct)
    {
        if (receipt.UploadedByUserId == userId) return true;

        var groupIds = await db.Expenses
            .Where(e => e.ReceiptId == receipt.Id)
            .Select(e => e.GroupId)
            .Union(db.Settlements.Where(s => s.ReceiptId == receipt.Id).Select(s => s.GroupId))
            .ToListAsync(ct);

        if (groupIds.Count == 0) return false;

        return await db.GroupMembers.AnyAsync(m =>
            groupIds.Contains(m.GroupId)
            && m.UserId == userId
            && m.Status == MembershipStatus.Active
            && !m.IsDeleted, ct);
    }

    private static ReceiptDto Map(Receipt receipt)
        => new(receipt.Id, receipt.ContentType, receipt.SizeBytes, receipt.OriginalFileName, receipt.UploadedAt);
}
