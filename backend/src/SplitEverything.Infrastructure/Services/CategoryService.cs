using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Categories;
using SplitEverything.Application.Services;
using SplitEverything.Domain.Entities;
using SplitEverything.Infrastructure.Persistence;

namespace SplitEverything.Infrastructure.Services;

public sealed class CategoryService(AppDbContext db, IAdminService admins) : ICategoryService
{
    private const int MostCategories = 30;
    private const int MostKeywords = 40;

    public async Task<IReadOnlyList<CategoryDto>> GetForGroupAsync(
        Guid userId, Guid groupId, CancellationToken ct = default)
    {
        await GroupAccess.RequireMemberAsync(db, userId, groupId, ct);
        return await ResolveForGroupAsync(groupId, ct);
    }

    public async Task<IReadOnlyList<CategoryDto>> SetForGroupAsync(
        Guid userId, Guid groupId, SetCategoriesRequest request, CancellationToken ct = default)
    {
        await GroupAccess.RequireMemberAsync(db, userId, groupId, ct);
        var group = await GroupAccess.RequireGroupAsync(db, groupId, ct);
        GroupAccess.RequireWritable(group);

        var wanted = Clean(request.Categories);

        var existing = await db.Categories.Where(c => c.GroupId == groupId).ToListAsync(ct);
        db.Categories.RemoveRange(existing);
        db.Categories.AddRange(wanted.Select(c => Row(c, groupId)));

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        return await ResolveForGroupAsync(groupId, ct);
    }

    public async Task<IReadOnlyList<CategoryDto>> GetGlobalAsync(
        Guid userId, CancellationToken ct = default)
    {
        await RequireAdminAsync(userId, ct);
        return await ReadAsync(null, ct);
    }

    public async Task<IReadOnlyList<CategoryDto>> SetGlobalAsync(
        Guid userId, SetCategoriesRequest request, CancellationToken ct = default)
    {
        await RequireAdminAsync(userId, ct);

        var wanted = Clean(request.Categories);

        var existing = await db.Categories.Where(c => c.GroupId == null).ToListAsync(ct);
        db.Categories.RemoveRange(existing);
        db.Categories.AddRange(wanted.Select(c => Row(c, null)));

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        return await ReadAsync(null, ct);
    }

    private async Task<IReadOnlyList<CategoryDto>> ResolveForGroupAsync(
        Guid groupId, CancellationToken ct)
    {
        var mine = await ReadAsync(groupId, ct);
        return mine.Count > 0 ? mine : await ReadAsync(null, ct);
    }

    private async Task<IReadOnlyList<CategoryDto>> ReadAsync(Guid? groupId, CancellationToken ct)
    {
        var rows = await db.Categories
            .AsNoTracking()
            .Where(c => c.GroupId == groupId)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(ct);

        return rows
            .Select(c => new CategoryDto(
                c.Key, c.Name, c.IconName, c.ColorHex, c.SortOrder, ReadKeywords(c.KeywordsJson)))
            .ToList();
    }

    private async Task RequireAdminAsync(Guid userId, CancellationToken ct)
    {
        if (!await admins.IsAdminAsync(userId, ct))
            throw new ForbiddenException("This is for whoever runs this server.");
    }

    private static List<CleanCategory> Clean(IReadOnlyList<CategoryInputDto>? input)
    {
        var rows = input ?? [];
        if (rows.Count > MostCategories)
            throw new ValidationException($"A list of categories is limited to {MostCategories}.");

        var cleaned = new List<CleanCategory>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var name = GroupAccess.RequireText(row.Name, "Category name", 60);
            var key = Slug(string.IsNullOrWhiteSpace(row.Key) ? name : row.Key!);

            if (key.Length == 0)
                throw new ValidationException($"\"{name}\" needs letters or numbers in its name.");

            if (!taken.Add(key))
                throw new ValidationException($"There is more than one \"{name}\" in that list.");

            var keywords = (row.Keywords ?? [])
                .Select(word => word.Trim().ToLowerInvariant())
                .Where(word => word.Length > 0)
                .Distinct()
                .ToList();

            if (keywords.Count > MostKeywords)
                throw new ValidationException($"\"{name}\" is limited to {MostKeywords} keywords.");
            if (keywords.Any(word => word.Length > 60))
                throw new ValidationException($"A keyword in \"{name}\" is too long.");

            cleaned.Add(new CleanCategory(
                key,
                name,
                Trimmed(row.IconName, "Icon name", 48) ?? "tag",
                Trimmed(row.ColorHex, "Colour", 9) ?? "#64748b",
                cleaned.Count * 10,
                keywords));
        }

        return cleaned;
    }

    private static Category Row(CleanCategory category, Guid? groupId)
        => new()
        {
            Key = category.Key,
            Name = category.Name,
            IconName = category.IconName,
            ColorHex = category.ColorHex,
            SortOrder = category.SortOrder,
            GroupId = groupId,
            KeywordsJson = JsonSerializer.Serialize(category.Keywords),
        };

    private static string Slug(string value)
    {
        var folded = value.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var builder = new System.Text.StringBuilder(folded.Length);

        foreach (var character in folded)
        {
            var kind = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character);
            if (kind == System.Globalization.UnicodeCategory.NonSpacingMark) continue;

            if (char.IsAsciiLetterOrDigit(character)) builder.Append(character);
            else if (builder.Length > 0 && builder[^1] != '-') builder.Append('-');
        }

        var slug = builder.ToString().Trim('-');
        return slug.Length <= 48 ? slug : slug[..48].TrimEnd('-');
    }

    private static string? Trimmed(string? value, string field, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (trimmed.Length > maxLength)
            throw new ValidationException($"{field} must be at most {maxLength} characters.");
        return trimmed;
    }

    public static IReadOnlyList<string> ReadKeywords(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed record CleanCategory(
        string Key, string Name, string IconName, string ColorHex, int SortOrder,
        IReadOnlyList<string> Keywords);
}
