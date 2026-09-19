using System.Security.Cryptography;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Common;

namespace SplitEverything.Infrastructure.Storage;

public sealed class ReceiptStorageOptions
{
    public const string SectionName = "ReceiptStorage";

    public string RootPath { get; set; } = "/data/receipts";
}

public sealed class LocalDiskReceiptStorage : IReceiptStorage
{
    private readonly string _root;

    public LocalDiskReceiptStorage(ReceiptStorageOptions options)
    {
        _root = Path.GetFullPath(options.RootPath);
        Directory.CreateDirectory(_root);
    }

    public async Task<StoredReceipt> SaveAsync(
        Stream content, string contentType, string? fileName, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();

        if (bytes.Length == 0) throw new ValidationException("That file is empty.");

        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var key = BuildKey(hash, ExtensionFor(contentType, fileName));
        var path = Resolve(key);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (!File.Exists(path))
            await File.WriteAllBytesAsync(path, bytes, ct);

        return new StoredReceipt(key, bytes.Length, hash);
    }

    public Task<Stream?> OpenAsync(string storageKey, CancellationToken ct = default)
    {
        var path = Resolve(storageKey);
        return Task.FromResult<Stream?>(File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
            : null);
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        var path = Resolve(storageKey);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string storageKey, CancellationToken ct = default)
        => Task.FromResult(File.Exists(Resolve(storageKey)));

    private static string BuildKey(string hash, string extension)
        => $"{hash[..2]}/{hash[2..4]}/{hash}{extension}";

    private string Resolve(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
            throw new ValidationException("A storage key is required.");

        var combined = Path.GetFullPath(Path.Combine(_root, storageKey));

        if (!combined.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && combined != _root)
        {
            throw new ValidationException("That storage key is not valid.");
        }

        return combined;
    }

    private static string ExtensionFor(string contentType, string? fileName)
        => contentType.ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/heic" or "image/heif" => ".heic",
            "application/pdf" => ".pdf",
            _ => string.IsNullOrWhiteSpace(fileName) ? ".bin" : Path.GetExtension(fileName)
        };
}
