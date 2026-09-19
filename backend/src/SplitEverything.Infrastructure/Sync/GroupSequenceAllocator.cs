using Microsoft.EntityFrameworkCore;
using SplitEverything.Infrastructure.Persistence;

namespace SplitEverything.Infrastructure.Sync;

public interface IGroupSequenceAllocator
{
    Task<long> NextAsync(Guid groupId, CancellationToken ct = default);
}

public sealed class GroupSequenceAllocator(AppDbContext db) : IGroupSequenceAllocator
{
    public async Task<long> NextAsync(Guid groupId, CancellationToken ct = default)
    {
        var allocated = await db.Database
            .SqlQuery<long>($"""
                UPDATE groups
                SET sequence_counter = sequence_counter + 1
                WHERE id = {groupId}
                RETURNING sequence_counter AS "Value"
                """)
            .ToListAsync(ct);

        if (allocated.Count == 0)
            throw new InvalidOperationException($"Group {groupId} does not exist.");

        var next = allocated[0];

        var tracked = db.ChangeTracker.Entries<Domain.Entities.Group>()
            .FirstOrDefault(e => e.Entity.Id == groupId);
        if (tracked is not null)
        {
            var property = tracked.Property(g => g.SequenceCounter);
            property.OriginalValue = next;
            property.CurrentValue = next;
            property.IsModified = false;
        }

        return next;
    }
}
