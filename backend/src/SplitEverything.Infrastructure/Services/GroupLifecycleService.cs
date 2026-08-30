using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Application.Contracts.Sync;
using SplitEverything.Application.Services;
using SplitEverything.Domain.Common;
using SplitEverything.Domain.Entities;
using SplitEverything.Domain.Sync;
using SplitEverything.Infrastructure.Persistence;
using SplitEverything.Infrastructure.Sync;

namespace SplitEverything.Infrastructure.Services;

/// <summary>
/// Moves history between group logs without breaking causality.
///
/// The shared rules across all four operations:
/// - Entities move; they are never deleted and recreated. Ids, vector clocks and
///   revision chains survive, so a device that already knew a row recognises it.
/// - Log entries move too, keeping their original LineageId. That is what lets a
///   split later partition a merged log back apart instead of guessing.
/// - Moved entries are renumbered above the destination's cursor, because clients
///   pull "everything after N" and anything below that would never be delivered.
/// - The destination group's clock joins the clock of the history it received, so
///   the moved revisions do not read as unseen and re-conflict.
/// </summary>
public sealed class GroupLifecycleService(
    AppDbContext db,
    ISyncWriter writer,
    IActivityService activity,
    IClock clock) : IGroupLifecycleService
{
    public async Task<MergeGroupsResult> MergeAsync(
        Guid userId, MergeGroupsRequest request, CancellationToken ct = default)
    {
        if (request.SourceGroupId == request.TargetGroupId)
            throw new ValidationException("A group cannot be merged into itself.");

        await GroupAccess.RequireAdminAsync(db, userId, request.SourceGroupId, ct);
        await GroupAccess.RequireAdminAsync(db, userId, request.TargetGroupId, ct);

        var source = await GroupAccess.RequireGroupAsync(db, request.SourceGroupId, ct);
        var target = await GroupAccess.RequireGroupAsync(db, request.TargetGroupId, ct);
        GroupAccess.RequireWritable(target);

        if (!string.Equals(source.BaseCurrency, target.BaseCurrency, StringComparison.OrdinalIgnoreCase))
        {
            // Every stored base amount was computed against the group's own base
            // currency. Merging across currencies would reinterpret all of them.
            throw new ValidationException(
                $"Both groups must share a base currency to merge ({source.BaseCurrency} vs {target.BaseCurrency}).");
        }

        var deviceId = GroupService.DeviceFor(userId);
        var memberMap = await BuildMergeMemberMapAsync(source, target, request.MemberMapping, deviceId, userId, ct);

        var movedExpenses = await MoveExpensesAsync(
            db.Expenses.Where(e => e.GroupId == source.Id), target.Id, memberMap, recordOrigin: false, ct);

        var movedSettlements = await MoveSettlementsAsync(
            db.Settlements.Where(s => s.GroupId == source.Id), target.Id, memberMap, ct);

        var movedEntries = await MoveLogEntriesAsync(
            db.SyncLog.Where(e => e.GroupId == source.Id), source.Id, target.Id, ct);

        // Join the clocks so nothing the source knew reads as unseen in the target.
        target.Clock = target.Clock.Merge(source.Clock);

        source.IsArchived = true;
        source.ArchivedAt = clock.UtcNow;

        await writer.RecordMarkerAsync(target.Id, SyncEntityType.Group, source.Id,
            SyncOperation.Merge, deviceId, userId,
            new
            {
                mergedFrom = source.Id,
                mergedFromName = source.Name,
                mergedFromLineage = source.LineageId,
                movedExpenses, movedSettlements, movedEntries
            },
            counterpartGroupId: source.Id, ct: ct);

        var link = new GroupLineageLink
        {
            Kind = GroupLineageKind.Merge,
            SourceGroupId = source.Id,
            TargetGroupId = target.Id,
            MovedLineageId = source.LineageId,
            VectorClockJson = target.VectorClockJson,
            PerformedByUserId = userId,
            OccurredAt = clock.UtcNow,
            Note = request.Note?.Trim()
        };
        db.GroupLineageLinks.Add(link);

        await activity.RecordAsync(target.Id, ActivityKind.GroupMerged, userId, null,
            SyncEntityType.Group, source.Id,
            $"Merged {source.Name} into {target.Name}",
            new { movedExpenses, movedSettlements }, ct);

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        return new MergeGroupsResult(target.Id, source.Id, movedExpenses, movedSettlements, movedEntries, link.Id);
    }

    public async Task<SplitGroupResult> SplitAsync(
        Guid userId, SplitGroupRequest request, CancellationToken ct = default)
    {
        await GroupAccess.RequireAdminAsync(db, userId, request.SourceGroupId, ct);
        var source = await GroupAccess.RequireGroupAsync(db, request.SourceGroupId, ct);
        GroupAccess.RequireWritable(source);

        var name = GroupAccess.RequireText(request.NewGroupName, "New group name", 120);
        if (request.ExpenseIds.Count == 0 && (request.SettlementIds?.Count ?? 0) == 0)
            throw new ValidationException("Choose at least one expense or settlement to split out.");

        var expenses = await db.Expenses
            .Include(e => e.Splits)
            .Where(e => request.ExpenseIds.Contains(e.Id))
            .ToListAsync(ct);

        if (expenses.Count != request.ExpenseIds.Distinct().Count())
            throw new ValidationException("One of those expenses does not exist.");
        if (expenses.Any(e => e.GroupId != source.Id))
            throw new ValidationException("Every expense must belong to the group being split.");

        var settlements = await db.Settlements
            .Where(s => (request.SettlementIds ?? new List<Guid>()).Contains(s.Id))
            .ToListAsync(ct);
        if (settlements.Any(s => s.GroupId != source.Id))
            throw new ValidationException("Every settlement must belong to the group being split.");

        // Everyone touched by the moved history has to exist in the new group, or a
        // split would leave dangling payers and broken balances.
        var neededMemberIds = expenses
            .SelectMany(e => e.Splits.Select(s => s.MemberId).Append(e.PaidByMemberId))
            .Concat(settlements.SelectMany(s => new[] { s.FromMemberId, s.ToMemberId }))
            .Concat(request.MemberIds ?? [])
            .Distinct()
            .ToList();

        var sourceMembers = await db.GroupMembers
            .Where(m => m.GroupId == source.Id && neededMemberIds.Contains(m.Id))
            .ToListAsync(ct);

        var newGroup = new Group
        {
            Name = name,
            Description = source.Description,
            BaseCurrency = source.BaseCurrency,
            IconName = source.IconName,
            ColorHex = source.ColorHex,
            CreatedByUserId = userId,
            // A fresh lineage for anything written from here on, while the moved
            // entries keep the lineage they were born with.
            LineageId = Guid.CreateVersion7(),
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow
        };
        db.Groups.Add(newGroup);
        await db.SaveChangesAsync(ct);

        var memberMap = new Dictionary<Guid, Guid>();
        foreach (var member in sourceMembers)
        {
            var copy = new GroupMember
            {
                GroupId = newGroup.Id,
                UserId = member.UserId,
                DisplayName = member.DisplayName,
                Role = member.UserId == userId ? GroupRole.Owner : member.Role,
                Status = member.Status,
                JoinedAt = member.JoinedAt,
                // Carry the clock so the copied member is not a brand new causal
                // event on devices that already knew this person.
                Clock = member.Clock,
                CreatedAt = clock.UtcNow,
                UpdatedAt = clock.UtcNow
            };
            db.GroupMembers.Add(copy);
            memberMap[member.Id] = copy.Id;
        }

        if (!memberMap.Values.Any(id => db.GroupMembers.Local.Any(m => m.Id == id && m.UserId == userId)))
        {
            // The splitter must be able to see the group they just made.
            var actor = await db.GroupMembers.FirstAsync(m => m.GroupId == source.Id && m.UserId == userId, ct);
            if (!memberMap.ContainsKey(actor.Id))
            {
                var copy = new GroupMember
                {
                    GroupId = newGroup.Id,
                    UserId = userId,
                    DisplayName = actor.DisplayName,
                    Role = GroupRole.Owner,
                    Status = MembershipStatus.Active,
                    JoinedAt = clock.UtcNow,
                    CreatedAt = clock.UtcNow,
                    UpdatedAt = clock.UtcNow
                };
                db.GroupMembers.Add(copy);
                memberMap[actor.Id] = copy.Id;
            }
        }

        await db.SaveChangesAsync(ct);

        var movedExpenses = await MoveExpensesAsync(
            db.Expenses.Where(e => request.ExpenseIds.Contains(e.Id)),
            newGroup.Id, memberMap, recordOrigin: false, ct);

        var movedSettlements = settlements.Count == 0
            ? 0
            : await MoveSettlementsAsync(
                db.Settlements.Where(s => (request.SettlementIds ?? new List<Guid>()).Contains(s.Id)),
                newGroup.Id, memberMap, ct);

        var movedEntityIds = request.ExpenseIds
            .Concat(settlements.Select(s => s.Id))
            .Concat(await db.ExpenseComments
                .Where(c => request.ExpenseIds.Contains(c.ExpenseId))
                .Select(c => c.Id)
                .ToListAsync(ct))
            .ToList();

        var movedEntries = await MoveLogEntriesAsync(
            db.SyncLog.Where(e => e.GroupId == source.Id && movedEntityIds.Contains(e.EntityId)),
            source.Id, newGroup.Id, ct);

        // The new group inherits the causal knowledge of the history it took.
        var movedClock = expenses.Aggregate(VectorClock.Empty, (acc, e) => acc.Merge(e.Clock));
        newGroup.Clock = newGroup.Clock.Merge(movedClock).Merge(source.Clock);

        await writer.RecordMarkerAsync(source.Id, SyncEntityType.Group, newGroup.Id,
            SyncOperation.Split, GroupService.DeviceFor(userId), userId,
            new { splitInto = newGroup.Id, splitIntoName = name, movedExpenses, movedSettlements },
            counterpartGroupId: newGroup.Id, ct: ct);

        await writer.RecordMarkerAsync(newGroup.Id, SyncEntityType.Group, source.Id,
            SyncOperation.Split, GroupService.DeviceFor(userId), userId,
            new { splitFrom = source.Id, splitFromName = source.Name, splitFromLineage = source.LineageId },
            counterpartGroupId: source.Id, lineageId: source.LineageId, ct: ct);

        var link = new GroupLineageLink
        {
            Kind = GroupLineageKind.Split,
            SourceGroupId = source.Id,
            TargetGroupId = newGroup.Id,
            MovedLineageId = source.LineageId,
            VectorClockJson = newGroup.VectorClockJson,
            PerformedByUserId = userId,
            OccurredAt = clock.UtcNow,
            Note = request.Note?.Trim()
        };
        db.GroupLineageLinks.Add(link);

        await activity.RecordAsync(source.Id, ActivityKind.GroupSplit, userId, null,
            SyncEntityType.Group, newGroup.Id,
            $"Split {movedExpenses} expenses out of {source.Name} into {name}", ct: ct);

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        return new SplitGroupResult(source.Id, newGroup.Id, movedExpenses, movedSettlements, movedEntries, link.Id);
    }

    public async Task<TransferExpenseResult> TransferExpenseAsync(
        Guid userId, TransferExpenseRequest request, CancellationToken ct = default)
    {
        var expense = await db.Expenses
                          .Include(e => e.Splits)
                          .Include(e => e.Items)
                          .FirstOrDefaultAsync(e => e.Id == request.ExpenseId && !e.IsDeleted, ct)
                      ?? throw new NotFoundException($"Expense {request.ExpenseId}");

        var fromGroupId = expense.GroupId;
        if (fromGroupId == request.TargetGroupId)
            throw new ValidationException("That expense is already in this group.");

        await GroupAccess.RequireMemberAsync(db, userId, fromGroupId, ct);
        await GroupAccess.RequireMemberAsync(db, userId, request.TargetGroupId, ct);

        var source = await GroupAccess.RequireGroupAsync(db, fromGroupId, ct);
        var target = await GroupAccess.RequireGroupAsync(db, request.TargetGroupId, ct);
        GroupAccess.RequireWritable(source);
        GroupAccess.RequireWritable(target);

        if (!string.Equals(source.BaseCurrency, target.BaseCurrency, StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("Both groups must share a base currency to move an expense between them.");

        var memberMap = await BuildTransferMemberMapAsync(
            expense, fromGroupId, request.TargetGroupId, request.MemberMapping, ct);

        var movedRevisions = await db.ExpenseRevisions
            .Where(r => r.ExpenseId == expense.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.GroupId, request.TargetGroupId), ct);

        var movedComments = await db.ExpenseComments
            .Where(c => c.ExpenseId == expense.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.GroupId, request.TargetGroupId), ct);

        var commentIds = await db.ExpenseComments
            .Where(c => c.ExpenseId == expense.Id)
            .Select(c => c.Id)
            .ToListAsync(ct);

        var movedEntries = await MoveLogEntriesAsync(
            db.SyncLog.Where(e =>
                e.GroupId == fromGroupId
                && (e.EntityId == expense.Id || commentIds.Contains(e.EntityId))),
            fromGroupId, request.TargetGroupId, ct);

        // OriginGroupId is only set the first time, so a twice-moved expense still
        // points at where it was actually created.
        expense.OriginGroupId ??= fromGroupId;
        expense.GroupId = request.TargetGroupId;
        expense.PaidByMemberId = memberMap[expense.PaidByMemberId];

        foreach (var split in expense.Splits)
        {
            split.MemberId = memberMap[split.MemberId];
            split.GroupId = request.TargetGroupId;
        }
        foreach (var item in expense.Items) item.GroupId = request.TargetGroupId;

        foreach (var share in await db.ExpenseItemShares
                     .Where(s => expense.Items.Select(i => i.Id).Contains(s.ExpenseItemId))
                     .ToListAsync(ct))
        {
            share.MemberId = memberMap[share.MemberId];
        }

        target.Clock = target.Clock.Merge(expense.Clock);

        var seq = await writer.RecordAsync(expense, SyncEntityType.Expense, request.TargetGroupId,
            SyncOperation.Transfer, GroupService.DeviceFor(userId), userId,
            ExpenseService.ExpensePayload(expense),
            sourceGroupId: fromGroupId,
            lineageId: expense.OriginLineageId, ct: ct);

        // The source log needs its own tombstone-style entry, or a device following
        // only the old group would keep showing an expense that has left.
        await writer.RecordMarkerAsync(fromGroupId, SyncEntityType.Expense, expense.Id,
            SyncOperation.Transfer, GroupService.DeviceFor(userId), userId,
            new { movedTo = request.TargetGroupId, expense.Id },
            counterpartGroupId: request.TargetGroupId, ct: ct);

        await activity.RecordAsync(request.TargetGroupId, ActivityKind.ExpenseTransferred, userId, null,
            SyncEntityType.Expense, expense.Id,
            $"Moved {expense.Description} from {source.Name} to {target.Name}", ct: ct);

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        return new TransferExpenseResult(
            expense.Id, fromGroupId, request.TargetGroupId, movedRevisions, movedComments, movedEntries + 1);
    }

    public async Task<CompactionResult> CompactAsync(
        Guid groupId, DateTimeOffset cutoff, CancellationToken ct = default)
    {
        var group = await GroupAccess.RequireGroupAsync(db, groupId, ct);

        var stale = await db.SyncLog
            .Where(e => e.GroupId == groupId && e.CreatedAt < cutoff && e.SupersededBySnapshotId == null)
            .OrderBy(e => e.ServerSeq)
            .ToListAsync(ct);

        if (stale.Count == 0)
            return new CompactionResult(groupId, null, 0, 0, 0);

        var upTo = stale[^1].ServerSeq;
        var joined = stale.Aggregate(VectorClock.Empty,
            (acc, entry) => acc.Merge(VectorClock.FromJson(entry.VectorClockJson)));

        // The snapshot has to stand alone: a device bootstrapping from it never sees
        // the entries it replaced, so the surviving state goes in whole.
        var state = new
        {
            groupId,
            group.BaseCurrency,
            cutoffAt = cutoff,
            upToServerSeq = upTo,
            members = await db.GroupMembers
                .Where(m => m.GroupId == groupId && !m.IsDeleted)
                .Select(m => new { m.Id, m.UserId, m.DisplayName, Role = (int)m.Role, Status = (int)m.Status })
                .ToListAsync(ct),
            expenses = await db.Expenses
                .Where(e => e.GroupId == groupId && !e.IsDeleted && e.ServerSeq <= upTo)
                .Select(e => new
                {
                    e.Id, e.PaidByMemberId, e.Description, e.Amount, e.Currency,
                    e.AmountInBaseCurrency, e.SpentAt, e.Revision,
                    Splits = e.Splits.Where(s => !s.IsDeleted)
                        .Select(s => new { s.MemberId, s.Amount, s.AmountInBaseCurrency }).ToList()
                })
                .ToListAsync(ct),
            settlements = await db.Settlements
                .Where(s => s.GroupId == groupId && !s.IsDeleted && s.ServerSeq <= upTo)
                .Select(s => new { s.Id, s.FromMemberId, s.ToMemberId, s.Amount, s.AmountInBaseCurrency, s.SettledAt })
                .ToListAsync(ct)
        };

        var snapshot = new SyncSnapshot
        {
            GroupId = groupId,
            UpToServerSeq = upTo,
            CutoffAt = cutoff,
            VectorClockJson = joined.ToJson(),
            StateJson = JsonSerializer.Serialize(state, SyncPayloads.Options),
            CompactedEntryCount = stale.Count,
            CreatedAt = clock.UtcNow
        };
        db.SyncSnapshots.Add(snapshot);
        await db.SaveChangesAsync(ct);

        var trimmed = await db.SyncLog
            .Where(e => e.GroupId == groupId && e.ServerSeq <= upTo)
            .ExecuteDeleteAsync(ct);

        snapshot.TrimmedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        return new CompactionResult(groupId, snapshot.Id, stale.Count, trimmed, upTo);
    }

    // ---- shared movement helpers -----------------------------------------

    private async Task<Dictionary<Guid, Guid>> BuildMergeMemberMapAsync(
        Group source, Group target, IReadOnlyDictionary<Guid, Guid>? explicitMapping,
        string deviceId, Guid userId, CancellationToken ct)
    {
        var sourceMembers = await db.GroupMembers
            .Where(m => m.GroupId == source.Id && !m.IsDeleted)
            .ToListAsync(ct);
        var targetMembers = await db.GroupMembers
            .Where(m => m.GroupId == target.Id && !m.IsDeleted)
            .ToListAsync(ct);

        var map = new Dictionary<Guid, Guid>();

        foreach (var member in sourceMembers)
        {
            if (explicitMapping?.TryGetValue(member.Id, out var mapped) == true)
            {
                map[member.Id] = mapped;
                continue;
            }

            // Same signed-in person is unambiguous; otherwise fall back to the name,
            // which is all a names-only import ever gave us.
            var match = member.UserId is not null
                ? targetMembers.FirstOrDefault(t => t.UserId == member.UserId)
                : targetMembers.FirstOrDefault(t =>
                    string.Equals(t.DisplayName, member.DisplayName, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                map[member.Id] = match.Id;
                continue;
            }

            // Nobody to match: carry the person over rather than dropping their history.
            var carried = new GroupMember
            {
                GroupId = target.Id,
                UserId = member.UserId,
                DisplayName = member.DisplayName,
                Role = GroupRole.Member,
                Status = member.Status,
                JoinedAt = member.JoinedAt,
                Clock = member.Clock,
                CreatedAt = clock.UtcNow,
                UpdatedAt = clock.UtcNow
            };
            db.GroupMembers.Add(carried);
            await db.SaveChangesAsync(ct);

            await writer.RecordAsync(carried, SyncEntityType.GroupMember, target.Id,
                SyncOperation.Create, deviceId, userId, GroupService.MemberPayload(carried), ct: ct);

            targetMembers.Add(carried);
            map[member.Id] = carried.Id;
        }

        return map;
    }

    private async Task<Dictionary<Guid, Guid>> BuildTransferMemberMapAsync(
        Expense expense, Guid fromGroupId, Guid targetGroupId,
        IReadOnlyDictionary<Guid, Guid>? explicitMapping, CancellationToken ct)
    {
        var involved = expense.Splits.Select(s => s.MemberId)
            .Append(expense.PaidByMemberId)
            .Distinct()
            .ToList();

        var sourceMembers = await db.GroupMembers
            .Where(m => m.GroupId == fromGroupId && involved.Contains(m.Id))
            .ToListAsync(ct);
        var targetMembers = await db.GroupMembers
            .Where(m => m.GroupId == targetGroupId && !m.IsDeleted)
            .ToListAsync(ct);

        var map = new Dictionary<Guid, Guid>();

        foreach (var member in sourceMembers)
        {
            if (explicitMapping?.TryGetValue(member.Id, out var mapped) == true)
            {
                if (targetMembers.All(t => t.Id != mapped))
                    throw new ValidationException("The mapped member is not in the destination group.");
                map[member.Id] = mapped;
                continue;
            }

            var match = member.UserId is not null
                ? targetMembers.FirstOrDefault(t => t.UserId == member.UserId)
                : targetMembers.FirstOrDefault(t =>
                    string.Equals(t.DisplayName, member.DisplayName, StringComparison.OrdinalIgnoreCase));

            // Refuse rather than guess: reassigning a debt to the wrong person is
            // worse than making the user pick.
            map[member.Id] = match?.Id ?? throw new ValidationException(
                $"{member.DisplayName} has no match in the destination group. Map them explicitly.");
        }

        return map;
    }

    private async Task<int> MoveExpensesAsync(
        IQueryable<Expense> query, Guid targetGroupId,
        Dictionary<Guid, Guid> memberMap, bool recordOrigin, CancellationToken ct)
    {
        var expenses = await query.Include(e => e.Splits).Include(e => e.Items).ToListAsync(ct);
        if (expenses.Count == 0) return 0;

        var expenseIds = expenses.Select(e => e.Id).ToList();

        foreach (var expense in expenses)
        {
            if (recordOrigin) expense.OriginGroupId ??= expense.GroupId;
            expense.GroupId = targetGroupId;
            expense.PaidByMemberId = Remap(memberMap, expense.PaidByMemberId);

            foreach (var split in expense.Splits)
            {
                split.MemberId = Remap(memberMap, split.MemberId);
                split.GroupId = targetGroupId;
            }
            foreach (var item in expense.Items) item.GroupId = targetGroupId;
        }

        foreach (var share in await db.ExpenseItemShares
                     .Where(s => db.ExpenseItems
                         .Where(i => expenseIds.Contains(i.ExpenseId))
                         .Select(i => i.Id)
                         .Contains(s.ExpenseItemId))
                     .ToListAsync(ct))
        {
            share.MemberId = Remap(memberMap, share.MemberId);
        }

        await db.ExpenseRevisions
            .Where(r => expenseIds.Contains(r.ExpenseId))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.GroupId, targetGroupId), ct);

        foreach (var comment in await db.ExpenseComments
                     .Where(c => expenseIds.Contains(c.ExpenseId))
                     .ToListAsync(ct))
        {
            comment.GroupId = targetGroupId;
            comment.AuthorMemberId = Remap(memberMap, comment.AuthorMemberId);
        }

        await db.SaveChangesAsync(ct);
        return expenses.Count;
    }

    private async Task<int> MoveSettlementsAsync(
        IQueryable<Settlement> query, Guid targetGroupId,
        Dictionary<Guid, Guid> memberMap, CancellationToken ct)
    {
        var settlements = await query.ToListAsync(ct);
        foreach (var settlement in settlements)
        {
            settlement.GroupId = targetGroupId;
            settlement.FromMemberId = Remap(memberMap, settlement.FromMemberId);
            settlement.ToMemberId = Remap(memberMap, settlement.ToMemberId);
        }

        await db.SaveChangesAsync(ct);
        return settlements.Count;
    }

    /// <summary>
    /// Moves log entries into another group, renumbering them above that group's
    /// current cursor while keeping their relative order and their lineage.
    /// </summary>
    private async Task<int> MoveLogEntriesAsync(
        IQueryable<SyncLogEntry> query, Guid fromGroupId, Guid targetGroupId, CancellationToken ct)
    {
        var entries = await query.OrderBy(e => e.ServerSeq).ToListAsync(ct);
        if (entries.Count == 0) return 0;

        var target = await db.Groups.FirstAsync(g => g.Id == targetGroupId, ct);
        var next = target.SequenceCounter;

        foreach (var entry in entries)
        {
            entry.GroupId = targetGroupId;
            entry.ServerSeq = ++next;
            entry.SourceGroupId ??= fromGroupId;
            // LineageId is deliberately untouched.
        }

        target.SequenceCounter = next;
        await db.SaveChangesAsync(ct);

        // Keep the entities' own cursors in step with their relocated log entries so
        // "everything after N" still finds them.
        foreach (var entry in entries)
        {
            switch (entry.EntityType)
            {
                case SyncEntityType.Expense:
                    await db.Expenses.Where(e => e.Id == entry.EntityId)
                        .ExecuteUpdateAsync(s => s.SetProperty(e => e.ServerSeq, entry.ServerSeq), ct);
                    break;
                case SyncEntityType.Settlement:
                    await db.Settlements.Where(e => e.Id == entry.EntityId)
                        .ExecuteUpdateAsync(s => s.SetProperty(e => e.ServerSeq, entry.ServerSeq), ct);
                    break;
                case SyncEntityType.ExpenseComment:
                    await db.ExpenseComments.Where(e => e.Id == entry.EntityId)
                        .ExecuteUpdateAsync(s => s.SetProperty(e => e.ServerSeq, entry.ServerSeq), ct);
                    break;
            }
        }

        return entries.Count;
    }

    private static Guid Remap(Dictionary<Guid, Guid> map, Guid memberId)
        => map.TryGetValue(memberId, out var mapped) ? mapped : memberId;
}
