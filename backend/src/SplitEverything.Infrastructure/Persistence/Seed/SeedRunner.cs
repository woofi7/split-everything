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

        var missing = CategorySeed.BuildSystemCategories()
            .Where(c => !have.Contains(c.Id))
            .ToList();

        if (missing.Count == 0) return;

        db.Categories.AddRange(missing);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }
}
