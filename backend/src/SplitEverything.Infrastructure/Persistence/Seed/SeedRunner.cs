using Microsoft.EntityFrameworkCore;

namespace SplitEverything.Infrastructure.Persistence.Seed;

public static class SeedRunner
{
    /// <summary>
    /// Idempotent: inserts only the system categories that are missing, keyed by
    /// deterministic id, so it is safe on every start and after a category is added
    /// to the seed list in a later release.
    /// </summary>
    public static async Task RunAsync(AppDbContext db, CancellationToken ct = default)
    {
        var existing = await db.Categories
            .Where(c => c.OwnerUserId == null)
            .Select(c => c.Id)
            .ToListAsync(ct);
        var have = existing.ToHashSet();

        var wanted = CategorySeed.BuildSystemCategories().ToList();

        var missing = wanted.Where(c => !have.Contains(c.Id)).ToList();
        if (missing.Count > 0) db.Categories.AddRange(missing);

        // Bring the shipped presentation of an existing category forward. The key
        // and id are the identity; the icon, colour and order are ours to update,
        // and a release that changes them should not leave old rows behind.
        var byId = wanted.ToDictionary(c => c.Id);
        var stale = await db.Categories
            .Where(c => c.OwnerUserId == null && have.Contains(c.Id))
            .ToListAsync(ct);

        foreach (var stored in stale)
        {
            if (!byId.TryGetValue(stored.Id, out var shipped)) continue;

            stored.Name = shipped.Name;
            stored.IconName = shipped.IconName;
            stored.ColorHex = shipped.ColorHex;
            stored.SortOrder = shipped.SortOrder;
        }

        if (missing.Count == 0 && !db.ChangeTracker.HasChanges()) return;

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }
}
