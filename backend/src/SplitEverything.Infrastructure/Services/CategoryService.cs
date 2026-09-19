using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Categories;
using SplitEverything.Application.Services;
using SplitEverything.Domain.Entities;
using SplitEverything.Infrastructure.Persistence;

namespace SplitEverything.Infrastructure.Services;

/// <summary>
/// The categories an expense can be filed under.
///
/// One table, two scopes. Rows with no group are the server's own list; rows with
/// one belong to that group. A group has no rows until somebody there edits the
/// list, and the edit takes a copy of whatever the server's list was at that
/// moment - so a household that renamed "Dining out" to "Resto" keeps it, and a
/// later change to the server's list leaves them alone.
///
/// Whole lists in and out, like the names left out of the totals: this is edited as
/// a list on one screen, the order of that list is the order it is shown in, and a
/// patch of one line would be a merge nobody asked for.
/// </summary>
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
        // A member, not an admin. It decides how spending is filed on a screen
        // everybody reads and changes nothing about the money, which is the same
        // line the names left out of the totals are drawn on.
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

    /// <summary>The group's own list, or the server's for a group that has not edited it.</summary>
    private async Task<IReadOnlyList<CategoryDto>> ResolveForGroupAsync(
        Guid groupId, CancellationToken ct)
    {
        var mine = await ReadAsync(groupId, ct);
        return mine.Count > 0 ? mine : await ReadAsync(null, ct);
    }

    private async Task<IReadOnlyList<CategoryDto>> ReadAsync(Guid? groupId, CancellationToken ct)
    {
        // Read the rows, then read the keywords out of them here: the JSON is ours
        // to parse and not something to ask the database to understand.
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

    /// <summary>
    /// A cleaned list, in the order it was sent.
    ///
    /// The order is the sort order: the editor is a list somebody arranges, and
    /// asking them for a number as well would be asking them to say the same thing
    /// twice. Keys are made from names when none was sent, which is what happens
    /// for a category being invented, and made unique so two "Ski" rows cannot
    /// collide and lose one another's expenses.
    /// </summary>
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

            // Two rows that would answer to the same key are two rows that would
            // silently share their expenses.
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

    /// <summary>
    /// A key from a name: lower case, letters and digits, dashes for the rest.
    ///
    /// Accents are folded rather than dropped, so "Épicerie" is "epicerie" and not
    /// "picerie" - this is written in French as often as in English.
    /// </summary>
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
            // A hand-edited row is not a reason to fail the screen.
            return [];
        }
    }

    private sealed record CleanCategory(
        string Key, string Name, string IconName, string ColorHex, int SortOrder,
        IReadOnlyList<string> Keywords);
}
