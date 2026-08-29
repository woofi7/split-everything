using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Sync;
using SplitEverything.Application.Services;
using SplitEverything.Domain.Common;
using SplitEverything.Domain.Entities;
using SplitEverything.Domain.Sync;
using SplitEverything.Infrastructure.Persistence;
using SplitEverything.Infrastructure.Sync;

namespace SplitEverything.Infrastructure.Services;

/// <summary>
/// Replication endpoint for offline clients.
///
/// Push applies a queue of operations one at a time. Each is judged on its vector
/// clock alone: newer wins, older is dropped as already-superseded, concurrent is
/// recorded as a conflict and left for a human. Nothing is ever overwritten
/// silently, and one bad operation never fails the batch - the rest of a device's
/// queue still has to drain.
/// </summary>
public sealed class SyncService(
    AppDbContext db,
    ISyncWriter writer,
    ISyncBroadcaster broadcaster,
    IClock clock) : ISyncService
{
    public async Task<SyncPushResult> PushAsync(
        Guid userId, SyncPushRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceId))
            throw new ValidationException("A device id is required to sync.");

        var accepted = new List<SyncAcceptedDto>();
        var conflicts = new List<SyncConflictDto>();
        var rejected = new List<SyncRejectedDto>();
        var touchedGroups = new HashSet<Guid>();

        var membership = await MembershipAsync(userId, ct);

        foreach (var operation in request.Operations)
        {
            try
            {
                if (!membership.Contains(operation.GroupId))
                {
                    rejected.Add(new SyncRejectedDto(operation.OperationId, operation.EntityId,
                        "You are not a member of this group.", "Forbidden"));
                    continue;
                }

                var outcome = await ApplyAsync(userId, request.DeviceId, operation, ct);

                switch (outcome.Kind)
                {
                    case OutcomeKind.Accepted:
                        accepted.Add(new SyncAcceptedDto(
                            operation.OperationId, operation.EntityId, outcome.ServerSeq, outcome.Clock!.Counters));
                        touchedGroups.Add(operation.GroupId);
                        break;
                    case OutcomeKind.AlreadyApplied:
                        break;
                    case OutcomeKind.Conflict:
                        conflicts.Add(outcome.Conflict!);
                        break;
                    case OutcomeKind.Rejected:
                        rejected.Add(new SyncRejectedDto(operation.OperationId, operation.EntityId,
                            outcome.Reason!, outcome.Code!));
                        break;
                }

                await db.SaveChangesAsync(ct);
            }
            catch (AppException ex)
            {
                // A single unacceptable operation is reported and skipped; failing the
                // batch would strand every later change in the device's queue.
                db.ChangeTracker.Clear();
                rejected.Add(new SyncRejectedDto(operation.OperationId, operation.EntityId, ex.Message, ex.Code));
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }

        var cursors = await CursorsAsync(membership, ct);
        var result = new SyncPushResult(accepted, conflicts, rejected, cursors);

        foreach (var groupId in touchedGroups)
            await broadcaster.BroadcastAsync(groupId, result, request.DeviceId, ct);

        foreach (var conflict in conflicts)
            await broadcaster.NotifyConflictAsync(conflict.GroupId, userId, conflict, ct);

        return result;
    }

    public async Task<SyncPullResult> PullAsync(
        Guid userId, SyncPullRequest request, CancellationToken ct = default)
    {
        var membership = await MembershipAsync(userId, ct);

        var requested = request.GroupCursors.Count > 0
            ? request.GroupCursors.Where(kv => membership.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value)
            : membership.ToDictionary(id => id, _ => 0L);

        var maxEntries = Math.Clamp(request.MaxEntries, 1, 2000);
        var entries = new List<SyncLogEntryDto>();
        var cursors = new Dictionary<Guid, long>();
        var snapshots = new List<SyncSnapshotDto>();
        var hasMore = false;

        foreach (var (groupId, cursor) in requested.OrderBy(kv => kv.Key))
        {
            var remaining = maxEntries - entries.Count;
            if (remaining <= 0)
            {
                hasMore = true;
                cursors[groupId] = cursor;
                continue;
            }

            // A device behind a compaction cutoff cannot replay trimmed entries, so
            // it bootstraps from the snapshot that replaced them instead.
            var snapshot = await db.SyncSnapshots
                .Where(s => s.GroupId == groupId && s.UpToServerSeq > cursor && s.TrimmedAt != null)
                .OrderByDescending(s => s.UpToServerSeq)
                .FirstOrDefaultAsync(ct);

            var effectiveCursor = cursor;
            if (snapshot is not null)
            {
                snapshots.Add(new SyncSnapshotDto(
                    snapshot.Id, snapshot.GroupId, snapshot.UpToServerSeq, snapshot.CutoffAt,
                    VectorClock.FromJson(snapshot.VectorClockJson).Counters, snapshot.StateJson));
                effectiveCursor = snapshot.UpToServerSeq;
            }

            var batch = await db.SyncLog
                .Where(e => e.GroupId == groupId && e.ServerSeq > effectiveCursor)
                .OrderBy(e => e.ServerSeq)
                .Take(remaining + 1)
                .ToListAsync(ct);

            if (batch.Count > remaining)
            {
                hasMore = true;
                batch.RemoveAt(batch.Count - 1);
            }

            entries.AddRange(batch.Select(e => new SyncLogEntryDto(
                e.ServerSeq, e.GroupId, e.EntityType, e.EntityId, e.Operation, e.DeviceId,
                e.PayloadJson, VectorClock.FromJson(e.VectorClockJson).Counters,
                e.LineageId, e.SourceGroupId, e.CounterpartGroupId, e.CreatedAt)));

            cursors[groupId] = batch.Count > 0
                ? batch[^1].ServerSeq
                : Math.Max(effectiveCursor, cursor);
        }

        return new SyncPullResult(entries, cursors, snapshots, hasMore);
    }

    public async Task<IReadOnlyList<SyncConflictDto>> GetOpenConflictsAsync(
        Guid userId, Guid? groupId = null, CancellationToken ct = default)
    {
        var membership = await MembershipAsync(userId, ct);
        if (groupId is not null && !membership.Contains(groupId.Value))
            throw new ForbiddenException("You are not a member of this group.");

        var scope = groupId is null ? membership : [groupId.Value];

        return await db.SyncConflicts
            .Where(c => scope.Contains(c.GroupId) && c.Resolution == ConflictResolution.Unresolved)
            .OrderBy(c => c.DetectedAt)
            .Select(c => new SyncConflictDto(
                c.Id, c.GroupId, c.EntityType, c.EntityId,
                c.StoredPayloadJson, new Dictionary<string, long>(),
                c.IncomingPayloadJson, new Dictionary<string, long>(),
                new List<string>(), c.DetectedAt))
            .ToListAsync(ct)
            .ContinueWith(task => (IReadOnlyList<SyncConflictDto>)task.Result
                .Select(Rehydrate)
                .ToList(), ct);
    }

    public async Task<SyncConflictDto> ResolveConflictAsync(
        Guid userId, ResolveConflictRequest request, CancellationToken ct = default)
    {
        var conflict = await db.SyncConflicts.FirstOrDefaultAsync(c => c.Id == request.ConflictId, ct)
                       ?? throw new NotFoundException($"Conflict {request.ConflictId}");

        await GroupAccess.RequireMemberAsync(db, userId, conflict.GroupId, ct);

        if (request.Resolution == ConflictResolution.Unresolved)
            throw new ValidationException("Pick a resolution.");
        if (request.Resolution == ConflictResolution.Merged && string.IsNullOrWhiteSpace(request.MergedPayloadJson))
            throw new ValidationException("A merged resolution needs the merged payload.");

        var deviceId = GroupService.DeviceFor(userId);

        if (request.Resolution is ConflictResolution.KeepRemote or ConflictResolution.Merged)
        {
            var payload = request.Resolution == ConflictResolution.Merged
                ? request.MergedPayloadJson!
                : conflict.IncomingPayloadJson;

            // Force the write through: the human has now ordered these revisions, so
            // the clock comparison that produced the conflict no longer applies.
            var operation = new SyncOperationDto(
                Guid.NewGuid(), conflict.EntityType, conflict.EntityId, SyncOperation.Update,
                conflict.GroupId, payload,
                VectorClock.FromJson(conflict.IncomingVectorClockJson).Counters,
                clock.UtcNow);

            var outcome = await ApplyAsync(userId, deviceId, operation, ct, force: true);
            if (outcome.Kind == OutcomeKind.Rejected)
                throw new ValidationException(outcome.Reason ?? "The resolved payload could not be applied.");
        }

        conflict.Resolution = request.Resolution;
        conflict.ResolvedByUserId = userId;
        conflict.ResolvedAt = clock.UtcNow;

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        return Rehydrate(new SyncConflictDto(
            conflict.Id, conflict.GroupId, conflict.EntityType, conflict.EntityId,
            conflict.StoredPayloadJson, VectorClock.FromJson(conflict.StoredVectorClockJson).Counters,
            conflict.IncomingPayloadJson, VectorClock.FromJson(conflict.IncomingVectorClockJson).Counters,
            JsonSerializer.Deserialize<List<string>>(conflict.ConflictingFieldsJson) ?? [],
            conflict.DetectedAt));
    }

    public async Task AcknowledgeAsync(
        Guid userId, string deviceId, IReadOnlyDictionary<Guid, long> groupCursors, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ValidationException("A device id is required.");

        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId, ct);
        if (device is null)
        {
            device = new Device { Id = deviceId, UserId = userId, CreatedAt = clock.UtcNow };
            db.Devices.Add(device);
        }
        else if (device.UserId != userId)
        {
            throw new ForbiddenException("This device belongs to another account.");
        }

        var highest = groupCursors.Count == 0 ? 0L : groupCursors.Values.Max();
        // Monotonic: a retried or out-of-order ack must never rewind the cursor and
        // make the device replay history it already applied.
        if (highest > device.LastAckedServerSeq) device.LastAckedServerSeq = highest;
        device.LastSyncedAt = clock.UtcNow;

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    // ---- application of a single operation --------------------------------

    private enum OutcomeKind { Accepted, AlreadyApplied, Conflict, Rejected }

    private sealed record Outcome(
        OutcomeKind Kind,
        long ServerSeq = 0,
        VectorClock? Clock = null,
        SyncConflictDto? Conflict = null,
        string? Reason = null,
        string? Code = null)
    {
        public static Outcome Reject(string reason, string code = "Invalid") => new(OutcomeKind.Rejected, Reason: reason, Code: code);
    }

    private async Task<Outcome> ApplyAsync(
        Guid userId, string deviceId, SyncOperationDto operation, CancellationToken ct, bool force = false)
    {
        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == operation.GroupId, ct);
        if (group is null) return Outcome.Reject("That group does not exist.", "NotFound");
        if (group.IsArchived) return Outcome.Reject("This group is archived.", "GroupArchived");

        return operation.EntityType switch
        {
            SyncEntityType.Expense => await ApplyExpenseAsync(userId, deviceId, operation, force, ct),
            SyncEntityType.Settlement => await ApplySettlementAsync(userId, deviceId, operation, force, ct),
            SyncEntityType.ExpenseComment => await ApplyCommentAsync(userId, deviceId, operation, force, ct),
            _ => Outcome.Reject($"{operation.EntityType} cannot be synced from a client.", "UnsupportedEntity")
        };
    }

    private async Task<Outcome> ApplyExpenseAsync(
        Guid userId, string deviceId, SyncOperationDto operation, bool force, CancellationToken ct)
    {
        var stored = await db.Expenses
            .Include(e => e.Splits)
            .FirstOrDefaultAsync(e => e.Id == operation.EntityId, ct);

        var incomingClock = VectorClock.From(operation.VectorClock);

        if (stored is not null && !force)
        {
            var decision = SyncArbiter.Decide(stored.Clock, incomingClock);
            if (decision == SyncDecision.AlreadyApplied) return new Outcome(OutcomeKind.AlreadyApplied);
            if (decision == SyncDecision.Conflict)
                return await RecordConflictAsync(userId, deviceId, operation, stored, ct);
        }

        if (operation.Operation == SyncOperation.Delete)
        {
            if (stored is null) return new Outcome(OutcomeKind.AlreadyApplied);

            var deleteSeq = await writer.RecordAsync(stored, SyncEntityType.Expense, operation.GroupId,
                SyncOperation.Delete, deviceId, userId, ExpenseService.ExpensePayload(stored), ct: ct);

            foreach (var split in stored.Splits.Where(s => !s.IsDeleted))
            {
                split.IsDeleted = true;
                split.DeletedAt = clock.UtcNow;
            }

            return new Outcome(OutcomeKind.Accepted, deleteSeq, stored.Clock);
        }

        var payload = SyncPayloads.Parse<SyncPayloads.ExpensePayload>(operation.PayloadJson);
        if (payload is null) return Outcome.Reject("The payload could not be read as JSON.", "InvalidPayload");
        if (string.IsNullOrWhiteSpace(payload.Description))
            return Outcome.Reject("An expense needs a description.", "InvalidPayload");
        if (payload.Amount is null or <= 0m)
            return Outcome.Reject("An expense needs a positive amount.", "InvalidPayload");
        if (payload.Splits.Count == 0)
            return Outcome.Reject("An expense needs at least one participant.", "InvalidPayload");

        var members = (await db.GroupMembers
            .Where(m => m.GroupId == operation.GroupId && !m.IsDeleted)
            .Select(m => m.Id).ToListAsync(ct)).ToHashSet();

        if (!members.Contains(payload.PaidByMemberId))
            return Outcome.Reject("The payer is not a member of this group.", "InvalidPayload");
        if (payload.Splits.Any(s => !members.Contains(s.MemberId)))
            return Outcome.Reject("A participant is not a member of this group.", "InvalidPayload");

        var currency = payload.Currency?.Trim().ToUpperInvariant() ?? "CAD";
        var splitsTotal = payload.Splits.Sum(s => s.Amount);
        if (Math.Abs(splitsTotal - payload.Amount.Value) > CurrencyPrecision.MinorUnit(currency))
            return Outcome.Reject("The splits do not add up to the expense total.", "InvalidPayload");

        var isNew = stored is null;
        stored ??= new Expense
        {
            Id = operation.EntityId,
            GroupId = operation.GroupId,
            CreatedAt = operation.ClientTimestamp,
            OriginLineageId = (await db.Groups.FirstAsync(g => g.Id == operation.GroupId, ct)).LineageId
        };

        stored.PaidByMemberId = payload.PaidByMemberId;
        stored.Description = payload.Description.Trim();
        stored.Amount = payload.Amount.Value;
        stored.Currency = currency;
        stored.AmountInBaseCurrency = payload.AmountInBaseCurrency ?? payload.Amount.Value;
        stored.ExchangeRate = payload.ExchangeRate ?? 1m;
        stored.SpentAt = payload.SpentAt ?? operation.ClientTimestamp;
        stored.CategoryId = payload.CategoryId;
        stored.SplitType = payload.SplitType ?? SplitType.Equal;
        stored.ReceiptId = payload.ReceiptId;
        stored.Notes = payload.Notes?.Trim();
        stored.IsDeleted = false;
        stored.DeletedAt = null;
        stored.Revision = isNew ? 1 : stored.Revision + 1;

        if (isNew) db.Expenses.Add(stored);

        ApplySplits(stored, payload);

        var seq = await writer.RecordAsync(stored, SyncEntityType.Expense, operation.GroupId,
            isNew ? SyncOperation.Create : SyncOperation.Update, deviceId, userId,
            ExpenseService.ExpensePayload(stored), ct: ct);

        db.ExpenseRevisions.Add(new ExpenseRevision
        {
            ExpenseId = stored.Id,
            GroupId = stored.GroupId,
            Revision = stored.Revision,
            EditedByUserId = userId,
            EditedByDeviceId = deviceId,
            EditedAt = clock.UtcNow,
            VectorClockJson = stored.VectorClockJson,
            SnapshotJson = JsonSerializer.Serialize(ExpenseService.ExpensePayload(stored)),
            ChangeSummary = isNew ? "Created offline" : "Edited offline"
        });

        return new Outcome(OutcomeKind.Accepted, seq, stored.Clock);
    }

    private void ApplySplits(Expense expense, SyncPayloads.ExpensePayload payload)
    {
        var wanted = payload.Splits.ToDictionary(s => s.MemberId);

        foreach (var split in expense.Splits.ToList())
        {
            if (wanted.TryGetValue(split.MemberId, out var incoming))
            {
                split.Amount = incoming.Amount;
                split.AmountInBaseCurrency = incoming.AmountInBaseCurrency ?? incoming.Amount;
                split.InputValue = incoming.InputValue;
                split.IsDeleted = false;
                split.DeletedAt = null;
                split.UpdatedAt = clock.UtcNow;
                wanted.Remove(split.MemberId);
            }
            else
            {
                split.IsDeleted = true;
                split.DeletedAt = clock.UtcNow;
            }
        }

        foreach (var incoming in wanted.Values)
        {
            expense.Splits.Add(new ExpenseSplit
            {
                ExpenseId = expense.Id,
                GroupId = expense.GroupId,
                MemberId = incoming.MemberId,
                Amount = incoming.Amount,
                AmountInBaseCurrency = incoming.AmountInBaseCurrency ?? incoming.Amount,
                InputValue = incoming.InputValue,
                Clock = expense.Clock,
                CreatedAt = clock.UtcNow,
                UpdatedAt = clock.UtcNow
            });
        }
    }

    private async Task<Outcome> ApplySettlementAsync(
        Guid userId, string deviceId, SyncOperationDto operation, bool force, CancellationToken ct)
    {
        var stored = await db.Settlements.FirstOrDefaultAsync(s => s.Id == operation.EntityId, ct);
        var incomingClock = VectorClock.From(operation.VectorClock);

        if (stored is not null && !force)
        {
            var decision = SyncArbiter.Decide(stored.Clock, incomingClock);
            if (decision == SyncDecision.AlreadyApplied) return new Outcome(OutcomeKind.AlreadyApplied);
            if (decision == SyncDecision.Conflict)
                return await RecordConflictAsync(userId, deviceId, operation, stored, ct,
                    SettlementService.SettlementPayload(stored));
        }

        if (operation.Operation == SyncOperation.Delete)
        {
            if (stored is null) return new Outcome(OutcomeKind.AlreadyApplied);
            var deleteSeq = await writer.RecordAsync(stored, SyncEntityType.Settlement, operation.GroupId,
                SyncOperation.Delete, deviceId, userId, SettlementService.SettlementPayload(stored), ct: ct);
            return new Outcome(OutcomeKind.Accepted, deleteSeq, stored.Clock);
        }

        var payload = SyncPayloads.Parse<SyncPayloads.SettlementPayload>(operation.PayloadJson);
        if (payload is null) return Outcome.Reject("The payload could not be read as JSON.", "InvalidPayload");
        if (payload.Amount is null or <= 0m)
            return Outcome.Reject("A settlement needs a positive amount.", "InvalidPayload");
        if (payload.FromMemberId == payload.ToMemberId)
            return Outcome.Reject("A settlement needs two different members.", "InvalidPayload");

        var members = (await db.GroupMembers
            .Where(m => m.GroupId == operation.GroupId && !m.IsDeleted)
            .Select(m => m.Id).ToListAsync(ct)).ToHashSet();

        if (!members.Contains(payload.FromMemberId) || !members.Contains(payload.ToMemberId))
            return Outcome.Reject("Both sides must be members of this group.", "InvalidPayload");

        var isNew = stored is null;
        stored ??= new Settlement
        {
            Id = operation.EntityId,
            GroupId = operation.GroupId,
            CreatedAt = operation.ClientTimestamp,
            OriginLineageId = (await db.Groups.FirstAsync(g => g.Id == operation.GroupId, ct)).LineageId
        };

        stored.FromMemberId = payload.FromMemberId;
        stored.ToMemberId = payload.ToMemberId;
        stored.Amount = payload.Amount.Value;
        stored.Currency = payload.Currency?.Trim().ToUpperInvariant() ?? "CAD";
        stored.AmountInBaseCurrency = payload.AmountInBaseCurrency ?? payload.Amount.Value;
        stored.SettledAt = payload.SettledAt ?? operation.ClientTimestamp;
        stored.Note = payload.Note?.Trim();
        stored.IsDeleted = false;
        stored.DeletedAt = null;

        if (isNew) db.Settlements.Add(stored);

        var seq = await writer.RecordAsync(stored, SyncEntityType.Settlement, operation.GroupId,
            isNew ? SyncOperation.Create : SyncOperation.Update, deviceId, userId,
            SettlementService.SettlementPayload(stored), ct: ct);

        return new Outcome(OutcomeKind.Accepted, seq, stored.Clock);
    }

    private async Task<Outcome> ApplyCommentAsync(
        Guid userId, string deviceId, SyncOperationDto operation, bool force, CancellationToken ct)
    {
        var stored = await db.ExpenseComments.FirstOrDefaultAsync(c => c.Id == operation.EntityId, ct);
        var incomingClock = VectorClock.From(operation.VectorClock);

        if (stored is not null && !force)
        {
            var decision = SyncArbiter.Decide(stored.Clock, incomingClock);
            if (decision == SyncDecision.AlreadyApplied) return new Outcome(OutcomeKind.AlreadyApplied);
            if (decision == SyncDecision.Conflict)
                return await RecordConflictAsync(userId, deviceId, operation, stored, ct,
                    new { stored.Id, stored.Body, stored.AuthorMemberId });
        }

        if (operation.Operation == SyncOperation.Delete)
        {
            if (stored is null) return new Outcome(OutcomeKind.AlreadyApplied);
            var deleteSeq = await writer.RecordAsync(stored, SyncEntityType.ExpenseComment, operation.GroupId,
                SyncOperation.Delete, deviceId, userId, new { stored.Id }, ct: ct);
            return new Outcome(OutcomeKind.Accepted, deleteSeq, stored.Clock);
        }

        var payload = SyncPayloads.Parse<SyncPayloads.CommentPayload>(operation.PayloadJson);
        if (payload is null) return Outcome.Reject("The payload could not be read as JSON.", "InvalidPayload");
        if (string.IsNullOrWhiteSpace(payload.Body))
            return Outcome.Reject("A comment needs a body.", "InvalidPayload");

        var expenseExists = await db.Expenses.AnyAsync(e =>
            e.Id == payload.ExpenseId && e.GroupId == operation.GroupId, ct);
        if (!expenseExists) return Outcome.Reject("That expense does not exist.", "NotFound");

        var memberExists = await db.GroupMembers.AnyAsync(m =>
            m.Id == payload.AuthorMemberId && m.GroupId == operation.GroupId, ct);
        if (!memberExists) return Outcome.Reject("The author is not a member of this group.", "InvalidPayload");

        var isNew = stored is null;
        stored ??= new ExpenseComment
        {
            Id = operation.EntityId,
            ExpenseId = payload.ExpenseId,
            GroupId = operation.GroupId,
            CreatedAt = operation.ClientTimestamp
        };

        stored.AuthorMemberId = payload.AuthorMemberId;
        stored.ParentCommentId = payload.ParentCommentId;
        stored.Body = payload.Body.Trim();
        if (!isNew) stored.EditedAt = clock.UtcNow;
        stored.IsDeleted = false;
        stored.DeletedAt = null;

        if (isNew) db.ExpenseComments.Add(stored);

        var seq = await writer.RecordAsync(stored, SyncEntityType.ExpenseComment, operation.GroupId,
            isNew ? SyncOperation.Create : SyncOperation.Update, deviceId, userId,
            new { stored.Id, stored.ExpenseId, stored.AuthorMemberId, stored.ParentCommentId, stored.Body }, ct: ct);

        return new Outcome(OutcomeKind.Accepted, seq, stored.Clock);
    }

    private async Task<Outcome> RecordConflictAsync(
        Guid userId, string deviceId, SyncOperationDto operation,
        SyncableEntity stored, CancellationToken ct, object? storedPayload = null)
    {
        var storedJson = storedPayload is not null
            ? JsonSerializer.Serialize(storedPayload, SyncPayloads.Options)
            : stored switch
            {
                Expense expense => JsonSerializer.Serialize(
                    ExpenseService.ExpensePayload(expense), SyncPayloads.Options),
                _ => "{}"
            };

        var fields = SyncArbiter.ConflictingFields(storedJson, operation.PayloadJson);

        var conflict = new SyncConflict
        {
            GroupId = operation.GroupId,
            EntityType = operation.EntityType,
            EntityId = operation.EntityId,
            StoredPayloadJson = storedJson,
            StoredVectorClockJson = stored.VectorClockJson,
            StoredDeviceId = stored.LastWriterDeviceId,
            IncomingPayloadJson = operation.PayloadJson,
            IncomingVectorClockJson = VectorClock.From(operation.VectorClock).ToJson(),
            IncomingDeviceId = deviceId,
            IncomingUserId = userId,
            ConflictingFieldsJson = JsonSerializer.Serialize(fields),
            DetectedAt = clock.UtcNow
        };
        db.SyncConflicts.Add(conflict);
        await db.SaveChangesAsync(ct);

        return new Outcome(OutcomeKind.Conflict, Conflict: new SyncConflictDto(
            conflict.Id, conflict.GroupId, conflict.EntityType, conflict.EntityId,
            storedJson, VectorClock.FromJson(conflict.StoredVectorClockJson).Counters,
            operation.PayloadJson, operation.VectorClock, fields, conflict.DetectedAt));
    }

    private async Task<HashSet<Guid>> MembershipAsync(Guid userId, CancellationToken ct)
        => (await db.GroupMembers
            .Where(m => m.UserId == userId && m.Status == MembershipStatus.Active && !m.IsDeleted)
            .Select(m => m.GroupId)
            .ToListAsync(ct)).ToHashSet();

    private async Task<Dictionary<Guid, long>> CursorsAsync(HashSet<Guid> groupIds, CancellationToken ct)
        => await db.Groups
            .Where(g => groupIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.SequenceCounter, ct);

    /// <summary>
    /// EF cannot project the jsonb clock columns through the DTO's dictionaries, so
    /// the list query returns placeholders and this fills them in.
    /// </summary>
    private SyncConflictDto Rehydrate(SyncConflictDto dto)
    {
        var row = db.SyncConflicts.AsNoTracking().First(c => c.Id == dto.ConflictId);
        return dto with
        {
            StoredVectorClock = VectorClock.FromJson(row.StoredVectorClockJson).Counters,
            IncomingVectorClock = VectorClock.FromJson(row.IncomingVectorClockJson).Counters,
            ConflictingFields = JsonSerializer.Deserialize<List<string>>(row.ConflictingFieldsJson) ?? []
        };
    }
}
