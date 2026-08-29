using Microsoft.EntityFrameworkCore;
using SplitEverything.Infrastructure.Persistence;

namespace SplitEverything.Infrastructure.Sync;

public interface IGroupSequenceAllocator
{
    /// <summary>Hands out the next sequence number for a group. Never repeats.</summary>
    Task<long> NextAsync(Guid groupId, CancellationToken ct = default);
}

/// <summary>
/// Per-group monotonic cursor for the sync log.
///
/// Allocated with a single atomic UPDATE ... RETURNING rather than read-modify-write
/// through the change tracker: Postgres takes a row lock for the duration, so
/// concurrent writers queue instead of both reading the same value. A duplicated
/// number would make a client's "everything after N" pull skip a change silently,
/// which is the one failure mode offline sync cannot recover from on its own.
/// </summary>
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

        // Keep any already-tracked copy of the group in step, so a caller that reads
        // the counter later in the same unit of work sees the allocation rather than
        // a stale value and hands the same number out twice.
        //
        // Both current and original value are set: marking the property unmodified
        // alone would make EF restore the current value from the original, quietly
        // undoing the update we just performed in the database.
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
