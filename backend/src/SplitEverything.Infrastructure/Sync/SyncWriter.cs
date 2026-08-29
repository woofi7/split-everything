using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Common;
using SplitEverything.Domain.Common;
using SplitEverything.Domain.Entities;
using SplitEverything.Infrastructure.Persistence;

namespace SplitEverything.Infrastructure.Sync;

public interface ISyncWriter
{
    /// <summary>
    /// Stamps a write onto an entity and appends the log entry peers will replay.
    /// Does not save: the caller decides the transaction boundary, so a multi-entity
    /// change (an expense plus its splits) lands as one unit.
    /// </summary>
    Task<long> RecordAsync<TEntity>(
        TEntity entity,
        SyncEntityType entityType,
        Guid groupId,
        SyncOperation operation,
        string deviceId,
        Guid? userId,
        object payload,
        Guid? sourceGroupId = null,
        Guid? counterpartGroupId = null,
        Guid? lineageId = null,
        bool allowArchived = false,
        CancellationToken ct = default)
        where TEntity : SyncableEntity;

    /// <summary>
    /// Appends a log entry with no entity behind it: the merge and split markers,
    /// and compaction snapshots.
    /// </summary>
    Task<long> RecordMarkerAsync(
        Guid groupId,
        SyncEntityType entityType,
        Guid entityId,
        SyncOperation operation,
        string deviceId,
        Guid? userId,
        object payload,
        Guid? counterpartGroupId = null,
        Guid? lineageId = null,
        CancellationToken ct = default);
}

public sealed class SyncWriter(
    AppDbContext db,
    IGroupSequenceAllocator sequences,
    IClock clock) : ISyncWriter
{
    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<long> RecordAsync<TEntity>(
        TEntity entity,
        SyncEntityType entityType,
        Guid groupId,
        SyncOperation operation,
        string deviceId,
        Guid? userId,
        object payload,
        Guid? sourceGroupId = null,
        Guid? counterpartGroupId = null,
        Guid? lineageId = null,
        bool allowArchived = false,
        CancellationToken ct = default)
        where TEntity : SyncableEntity
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ValidationException("A device id is required for every write.");

        var group = await LoadGroupAsync(groupId, ct);
        if (group.IsArchived && !allowArchived)
            throw new GroupArchivedException();

        var now = clock.UtcNow;
        entity.Clock = entity.Clock.Tick(deviceId);
        entity.LastWriterDeviceId = deviceId;
        entity.UpdatedAt = now;

        if (operation == SyncOperation.Delete)
        {
            entity.IsDeleted = true;
            entity.DeletedAt = now;
        }

        var seq = await sequences.NextAsync(groupId, ct);
        entity.ServerSeq = seq;

        db.SyncLog.Add(new SyncLogEntry
        {
            GroupId = groupId,
            ServerSeq = seq,
            EntityType = entityType,
            EntityId = entity.Id,
            Operation = operation,
            DeviceId = deviceId,
            UserId = userId,
            VectorClockJson = entity.VectorClockJson,
            PayloadJson = Serialize(payload),
            // Default to the group's own lineage. A transfer passes the origin
            // lineage instead, so a later split can find the moved history again.
            LineageId = lineageId ?? group.LineageId,
            SourceGroupId = sourceGroupId,
            CounterpartGroupId = counterpartGroupId,
            CreatedAt = now
        });

        return seq;
    }

    public async Task<long> RecordMarkerAsync(
        Guid groupId,
        SyncEntityType entityType,
        Guid entityId,
        SyncOperation operation,
        string deviceId,
        Guid? userId,
        object payload,
        Guid? counterpartGroupId = null,
        Guid? lineageId = null,
        CancellationToken ct = default)
    {
        var group = await LoadGroupAsync(groupId, ct);
        var seq = await sequences.NextAsync(groupId, ct);

        db.SyncLog.Add(new SyncLogEntry
        {
            GroupId = groupId,
            ServerSeq = seq,
            EntityType = entityType,
            EntityId = entityId,
            Operation = operation,
            DeviceId = deviceId,
            UserId = userId,
            VectorClockJson = group.VectorClockJson,
            PayloadJson = Serialize(payload),
            LineageId = lineageId ?? group.LineageId,
            CounterpartGroupId = counterpartGroupId,
            CreatedAt = clock.UtcNow
        });

        return seq;
    }

    private async Task<Group> LoadGroupAsync(Guid groupId, CancellationToken ct)
    {
        var group = db.ChangeTracker.Entries<Group>()
                        .FirstOrDefault(e => e.Entity.Id == groupId)?.Entity
                    ?? await db.Groups.FirstOrDefaultAsync(g => g.Id == groupId, ct);

        return group ?? throw new NotFoundException($"Group {groupId}");
    }

    private static string Serialize(object payload) => JsonSerializer.Serialize(payload, PayloadOptions);
}
