using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Application.Services;
using SplitEverything.Domain.Algorithms;
using SplitEverything.Domain.Common;
using SplitEverything.Domain.Entities;
using SplitEverything.Infrastructure.Persistence;
using SplitEverything.Infrastructure.Sync;

namespace SplitEverything.Infrastructure.Services;

public sealed class GroupService(
    AppDbContext db,
    ISyncWriter writer,
    IActivityService activity,
    IClock clock) : IGroupService
{
    public async Task<GroupDto> CreateAsync(Guid userId, CreateGroupRequest request, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new NotFoundException($"User {userId}");

        var name = GroupAccess.RequireText(request.Name, "Group name", 120);
        var currency = GroupAccess.NormalizeCurrency(request.BaseCurrency, "Base currency");

        var group = new Group
        {
            Name = name,
            Description = request.Description?.Trim(),
            BaseCurrency = currency,
            IconName = Clearable(request.IconName ?? string.Empty, "Icon name", 48),
            ColorHex = string.IsNullOrWhiteSpace(request.ColorHex) ? "#4f46e5" : request.ColorHex.Trim(),
            CreatedByUserId = userId,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow
        };
        db.Groups.Add(group);

        var owner = NewMember(group.Id, userId, user.DisplayName, GroupRole.Owner);
        db.GroupMembers.Add(owner);

        var placeholders = (request.PlaceholderMemberNames ?? [])
            .Select(n => n?.Trim())
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(n => NewMember(group.Id, null, n!, GroupRole.Member))
            .ToList();
        db.GroupMembers.AddRange(placeholders);

        // Save first so the group row exists: the sequence allocator updates it in
        // place, and the log entries carry a foreign key to it.
        await db.SaveChangesAsync(ct);

        var deviceId = DeviceFor(userId);
        await writer.RecordAsync(group, SyncEntityType.Group, group.Id, SyncOperation.Create,
            deviceId, userId, GroupPayload(group), ct: ct);

        foreach (var member in placeholders.Prepend(owner))
        {
            await writer.RecordAsync(member, SyncEntityType.GroupMember, group.Id, SyncOperation.Create,
                deviceId, userId, MemberPayload(member), ct: ct);
        }

        await activity.RecordAsync(group.Id, ActivityKind.GroupCreated, userId, owner.Id,
            SyncEntityType.Group, group.Id, $"{user.DisplayName} created {group.Name}", ct: ct);

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        return await GetAsync(userId, group.Id, ct);
    }

    public async Task<GroupDto> GetAsync(Guid userId, Guid groupId, CancellationToken ct = default)
    {
        await GroupAccess.RequireMemberAsync(db, userId, groupId, ct);
        var group = await GroupAccess.RequireGroupAsync(db, groupId, ct);

        var members = await db.GroupMembers
            .Where(m => m.GroupId == groupId && !m.IsDeleted)
            .Select(m => new
            {
                Member = m,
                AvatarUrl = m.User == null ? null : m.User.AvatarUrl
            })
            .ToListAsync(ct);

        var balances = await ComputeBalancesAsync(groupId, members.Select(m => m.Member.Id), group.BaseCurrency, ct);

        var totals = await db.Expenses
            .Where(e => e.GroupId == groupId && !e.IsDeleted)
            .GroupBy(_ => 1)
            .Select(g => new { Total = g.Sum(e => e.AmountInBaseCurrency), Count = g.Count() })
            .FirstOrDefaultAsync(ct);

        var myMemberId = members.FirstOrDefault(m => m.Member.UserId == userId)?.Member.Id;

        return new GroupDto(
            group.Id, group.Name, group.Description, group.BaseCurrency,
            group.IconName, group.ColorHex, group.IsArchived,
            group.SequenceCounter, group.LineageId, group.CreatedAt, group.UpdatedAt,
            members
                .OrderByDescending(m => m.Member.Role)
                .ThenBy(m => m.Member.DisplayName)
                .Select(m => new GroupMemberDto(
                    m.Member.Id, m.Member.UserId, m.Member.DisplayName, m.AvatarUrl,
                    m.Member.Role, m.Member.Status, m.Member.IsPlaceholder,
                    balances.GetValueOrDefault(m.Member.Id)))
                .ToList(),
            myMemberId is null ? 0m : balances.GetValueOrDefault(myMemberId.Value),
            totals?.Total ?? 0m,
            totals?.Count ?? 0,
            group.DefaultSplitType,
            ReadDefaultSplitValues(group.DefaultSplitValuesJson));
    }

    /// <summary>
    /// The stored default split values, or null. Unreadable JSON is treated as no
    /// default rather than as an error: it would only ever mean a shape from an
    /// older version, and refusing to load a group over it would be absurd.
    /// </summary>
    internal static IReadOnlyDictionary<Guid, decimal>? ReadDefaultSplitValues(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<Guid, decimal>>(json);
            return parsed is { Count: > 0 } ? parsed : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<GroupSummaryDto>> ListAsync(
        Guid userId, bool includeArchived = false, CancellationToken ct = default)
    {
        var rows = await db.GroupMembers
            .Where(m => m.UserId == userId && m.Status == MembershipStatus.Active && !m.IsDeleted)
            .Select(m => new
            {
                m.GroupId,
                MemberId = m.Id,
                m.Group!.Name,
                m.Group.BaseCurrency,
                m.Group.IconName,
                m.Group.ColorHex,
                m.Group.IsArchived,
                MemberCount = m.Group.Members.Count(x => !x.IsDeleted && x.Status == MembershipStatus.Active),
                LastActivityAt = db.ActivityLog
                    .Where(a => a.GroupId == m.GroupId)
                    .Max(a => (DateTimeOffset?)a.OccurredAt)
            })
            .Where(r => includeArchived || !r.IsArchived)
            .ToListAsync(ct);

        var summaries = new List<GroupSummaryDto>(rows.Count);
        foreach (var row in rows)
        {
            var memberIds = await db.GroupMembers
                .Where(m => m.GroupId == row.GroupId && !m.IsDeleted)
                .Select(m => m.Id)
                .ToListAsync(ct);

            var balances = await ComputeBalancesAsync(row.GroupId, memberIds, row.BaseCurrency, ct);

            summaries.Add(new GroupSummaryDto(
                row.GroupId, row.Name, row.BaseCurrency, row.IconName, row.ColorHex,
                row.IsArchived, balances.GetValueOrDefault(row.MemberId),
                row.MemberCount, row.LastActivityAt));
        }

        return summaries
            .OrderBy(s => s.IsArchived)
            .ThenBy(s => s.Name)
            .ToList();
    }

    public async Task<GroupDto> UpdateAsync(
        Guid userId, Guid groupId, UpdateGroupRequest request, CancellationToken ct = default)
    {
        await GroupAccess.RequireAdminAsync(db, userId, groupId, ct);
        var group = await GroupAccess.RequireGroupAsync(db, groupId, ct);
        GroupAccess.RequireWritable(group);

        if (request.Name is not null)
            group.Name = GroupAccess.RequireText(request.Name, "Group name", 120);

        // For the optional text fields, null means "not supplied" in a patch, so it
        // cannot also mean "clear". An empty string is the explicit clear -
        // otherwise the remove button in the icon picker would silently do nothing.
        if (request.Description is not null)
            group.Description = Clearable(request.Description, "Description", 2000);
        if (request.IconName is not null)
            group.IconName = Clearable(request.IconName, "Icon name", 48);
        if (request.ColorHex is not null)
            group.ColorHex = Clearable(request.ColorHex, "Colour", 9) ?? "#4f46e5";
        if (request.BaseCurrency is not null)
            group.BaseCurrency = GroupAccess.NormalizeCurrency(request.BaseCurrency, "Base currency");

        if (request.DefaultSplitType is { } defaultSplit)
        {
            group.DefaultSplitType = defaultSplit;

            // Only meaningful alongside a type. An equal split needs none, and an
            // empty map is the explicit clear, the same convention as the text
            // fields above.
            group.DefaultSplitValuesJson = defaultSplit == SplitType.Equal
                ? null
                : await BuildDefaultSplitValuesAsync(groupId, request.DefaultSplitValues, ct);
        }

        await writer.RecordAsync(group, SyncEntityType.Group, groupId, SyncOperation.Update,
            DeviceFor(userId), userId, GroupPayload(group), ct: ct);

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        return await GetAsync(userId, groupId, ct);
    }

    /// <summary>
    /// Reads an optional text field from a patch: trimmed, or null when the caller
    /// sent an empty string to clear it. Rejects anything too long for the column
    /// rather than letting the database truncate it into something meaningless.
    /// </summary>
    /// <summary>
    /// Validates and serialises the default split values.
    ///
    /// Members are checked against the group: a value for someone who is not in it
    /// would sit in the group forever, silently ignored by every form that read it.
    /// </summary>
    private async Task<string?> BuildDefaultSplitValuesAsync(
        Guid groupId, IReadOnlyDictionary<Guid, decimal>? values, CancellationToken ct)
    {
        if (values is null || values.Count == 0) return null;

        var members = (await db.GroupMembers
            .Where(m => m.GroupId == groupId && !m.IsDeleted)
            .Select(m => m.Id)
            .ToListAsync(ct)).ToHashSet();

        foreach (var (memberId, value) in values)
        {
            if (!members.Contains(memberId))
                throw new ValidationException("A default split value names someone who is not in this group.");
            if (value < 0m)
                throw new ValidationException("A default split value cannot be negative.");
        }

        return JsonSerializer.Serialize(values);
    }

    private static string? Clearable(string value, string field, int maxLength)
    {
        var trimmed = value.Trim();

        if (trimmed.Length == 0) return null;
        if (trimmed.Length > maxLength)
            throw new ValidationException($"{field} must be at most {maxLength} characters.");

        return trimmed;
    }

    public Task<GroupDto> ArchiveAsync(Guid userId, Guid groupId, CancellationToken ct = default)
        => SetArchivedAsync(userId, groupId, archived: true, ct);

    public Task<GroupDto> UnarchiveAsync(Guid userId, Guid groupId, CancellationToken ct = default)
        => SetArchivedAsync(userId, groupId, archived: false, ct);

    private async Task<GroupDto> SetArchivedAsync(Guid userId, Guid groupId, bool archived, CancellationToken ct)
    {
        var actor = await GroupAccess.RequireAdminAsync(db, userId, groupId, ct);
        var group = await GroupAccess.RequireGroupAsync(db, groupId, ct);

        if (group.IsArchived != archived)
        {
            group.IsArchived = archived;
            group.ArchivedAt = archived ? clock.UtcNow : null;

            // allowArchived: the unarchive write itself targets an archived group, so
            // the freeze must not lock the door behind it.
            await writer.RecordAsync(group, SyncEntityType.Group, groupId, SyncOperation.Update,
                DeviceFor(userId), userId, GroupPayload(group), allowArchived: true, ct: ct);

            await activity.RecordAsync(groupId,
                archived ? ActivityKind.GroupArchived : ActivityKind.GroupUnarchived,
                userId, actor.Id, SyncEntityType.Group, groupId,
                $"{actor.DisplayName} {(archived ? "archived" : "reopened")} {group.Name}", ct: ct);

            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }

        return await GetAsync(userId, groupId, ct);
    }

    public async Task<GroupMemberDto> AddUserMemberAsync(
        Guid userId, Guid groupId, AddUserMemberRequest request, CancellationToken ct = default)
    {
        var actor = await GroupAccess.RequireMemberAsync(db, userId, groupId, ct);
        var group = await GroupAccess.RequireGroupAsync(db, groupId, ct);
        GroupAccess.RequireWritable(group);

        var invitee = await db.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, ct)
                      ?? throw new NotFoundException($"User {request.UserId}");

        // Deliberately ignores the tombstone, the same as redeeming an invite:
        // someone who was removed and comes back has to reclaim their original row.
        // A second one would collide with the one-membership-per-user index and
        // orphan whatever history still points at the first.
        var existing = await db.GroupMembers
            .FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == invitee.Id, ct);

        if (existing is not null)
        {
            if (existing.Status == MembershipStatus.Active && !existing.IsDeleted)
                return MemberDto(existing, invitee.AvatarUrl);

            existing.Status = MembershipStatus.Active;
            existing.LeftAt = null;
            existing.IsDeleted = false;
            existing.DeletedAt = null;

            await writer.RecordAsync(existing, SyncEntityType.GroupMember, groupId,
                SyncOperation.Update, DeviceFor(userId), userId, MemberPayload(existing), ct: ct);

            await activity.RecordAsync(groupId, ActivityKind.MemberInvited, userId, actor.Id,
                SyncEntityType.GroupMember, existing.Id,
                $"{actor.DisplayName} added {invitee.DisplayName} back to {group.Name}", ct: ct);

            await db.SaveChangesAsync(ct);
            return MemberDto(existing, invitee.AvatarUrl);
        }

        var member = NewMember(groupId, invitee.Id, invitee.DisplayName, GroupRole.Member);
        db.GroupMembers.Add(member);

        await writer.RecordAsync(member, SyncEntityType.GroupMember, groupId, SyncOperation.Create,
            DeviceFor(userId), userId, MemberPayload(member), ct: ct);

        await activity.RecordAsync(groupId, ActivityKind.MemberInvited, userId, actor.Id,
            SyncEntityType.GroupMember, member.Id,
            $"{actor.DisplayName} added {invitee.DisplayName} to {group.Name}", ct: ct);

        await db.SaveChangesAsync(ct);

        return MemberDto(member, invitee.AvatarUrl);
    }

    public async Task<IReadOnlyList<AddableUserDto>> ListAddableUsersAsync(
        Guid userId, Guid? groupId, CancellationToken ct = default)
    {
        // Membership is the gate. Without it, naming a group id would tell a
        // stranger who is in it by omission.
        if (groupId is { } id) await GroupAccess.RequireMemberAsync(db, userId, id, ct);

        var taken = groupId is { } forGroup
            ? await db.GroupMembers
                .Where(m => m.GroupId == forGroup && !m.IsDeleted
                            && m.Status == MembershipStatus.Active && m.UserId != null)
                .Select(m => m.UserId!.Value)
                .ToListAsync(ct)
            : [];

        return await db.Users
            .Where(u => u.Id != userId && !taken.Contains(u.Id))
            .OrderBy(u => u.DisplayName)
            .Select(u => new AddableUserDto(u.Id, u.DisplayName, u.Email, u.AvatarUrl))
            .ToListAsync(ct);
    }

    private static GroupMemberDto MemberDto(GroupMember member, string? avatarUrl)
        => new(member.Id, member.UserId, member.DisplayName, avatarUrl,
            member.Role, member.Status, member.UserId is null, 0m);

    /// <summary>
    /// Folds one member into another.
    ///
    /// Everything the source paid, owed, was owed and said becomes the target's,
    /// and the source is removed. It exists because the same person can end up in
    /// a group twice: once as a name a CSV import invented, and again as the
    /// account they signed up with.
    ///
    /// One way only. Nothing records which rows moved, so nothing can move them
    /// back, which is why the caller has to mean it.
    /// </summary>
    public async Task<GroupDto> MergeMembersAsync(
        Guid userId, Guid groupId, MergeMembersRequest request, CancellationToken ct = default)
    {
        var actor = await GroupAccess.RequireAdminAsync(db, userId, groupId, ct);
        var group = await GroupAccess.RequireGroupAsync(db, groupId, ct);
        GroupAccess.RequireWritable(group);

        if (request.SourceMemberId == request.TargetMemberId)
            throw new ValidationException("Pick two different people to merge.");

        var source = await RequireMemberAsync(groupId, request.SourceMemberId, ct);
        var target = await RequireMemberAsync(groupId, request.TargetMemberId, ct);

        // The group has to keep an owner, and the owner is never the one to lose.
        if (source.Role == GroupRole.Owner)
            throw new ValidationException(
                "The group owner cannot be merged away. Merge the other person into the owner instead.");

        // The source may well be a removed member: removing one deactivates it
        // rather than deleting it precisely because it still holds expenses, and
        // folding that into a real account is what this is for. The target is the
        // other way round, because everything ends up on it and a removed member
        // is one nobody can see.
        if (target.Status == MembershipStatus.Removed)
            throw new ValidationException(
                $"{target.DisplayName} has been removed from this group, so nothing can be merged into them.");

        var deviceId = DeviceFor(userId);
        var summary = $"Merged {source.DisplayName} into {target.DisplayName}";

        // Expenses first, with everything hanging off them. A split and an item
        // share are both keyed by member and both may already exist for the
        // target, in which case the two become one rather than colliding on the
        // unique index they share.
        var expenses = await db.Expenses
            .Include(e => e.Splits)
            .Include(e => e.Items).ThenInclude(i => i.Shares)
            .Where(e => e.GroupId == groupId && (
                e.PaidByMemberId == source.Id ||
                e.Splits.Any(s => s.MemberId == source.Id) ||
                e.Items.Any(i => i.Shares.Any(sh => sh.MemberId == source.Id))))
            .ToListAsync(ct);

        foreach (var expense in expenses)
        {
            if (expense.PaidByMemberId == source.Id) expense.PaidByMemberId = target.Id;

            MergeSplits(expense, source.Id, target.Id);
            foreach (var item in expense.Items) MergeShares(item, source.Id, target.Id);

            expense.Revision++;
            db.ExpenseRevisions.Add(new ExpenseRevision
            {
                ExpenseId = expense.Id,
                GroupId = expense.GroupId,
                Revision = expense.Revision,
                EditedByUserId = userId,
                EditedByDeviceId = deviceId,
                EditedAt = clock.UtcNow,
                VectorClockJson = expense.VectorClockJson,
                SnapshotJson = JsonSerializer.Serialize(ExpenseService.ExpensePayload(expense)),
                ChangeSummary = summary
            });

            await writer.RecordAsync(expense, SyncEntityType.Expense, groupId, SyncOperation.Update,
                deviceId, userId, ExpenseService.ExpensePayload(expense), ct: ct);
        }

        var settlements = await db.Settlements
            .Where(s => s.GroupId == groupId
                        && (s.FromMemberId == source.Id || s.ToMemberId == source.Id))
            .ToListAsync(ct);

        foreach (var settlement in settlements)
        {
            if (settlement.FromMemberId == source.Id) settlement.FromMemberId = target.Id;
            if (settlement.ToMemberId == source.Id) settlement.ToMemberId = target.Id;

            // A payment from someone to themselves says nothing. It only ever meant
            // anything while the two were different people.
            var operation = settlement.FromMemberId == settlement.ToMemberId
                ? SyncOperation.Delete
                : SyncOperation.Update;

            await writer.RecordAsync(settlement, SyncEntityType.Settlement, groupId, operation,
                deviceId, userId, SettlementService.SettlementPayload(settlement), ct: ct);
        }

        var comments = await db.ExpenseComments
            .Where(c => c.GroupId == groupId && c.AuthorMemberId == source.Id)
            .ToListAsync(ct);

        foreach (var comment in comments)
        {
            comment.AuthorMemberId = target.Id;
            await writer.RecordAsync(comment, SyncEntityType.ExpenseComment, groupId,
                SyncOperation.Update, deviceId, userId,
                new { comment.Id, comment.ExpenseId, comment.AuthorMemberId, comment.ParentCommentId, comment.Body },
                ct: ct);
        }

        // Not synced, so repointed without a log entry, but still repointed: a
        // recurring expense left pointing at a removed member would come due and
        // fail, and the activity feed would name nobody.
        await db.RecurringExpenses
            .Where(r => r.GroupId == groupId && r.PaidByMemberId == source.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.PaidByMemberId, target.Id), ct);

        await db.ActivityLog
            .Where(a => a.GroupId == groupId && a.ActorMemberId == source.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.ActorMemberId, target.Id), ct);

        // An invite still out there would otherwise hand its taker a member row
        // that no longer exists.
        await db.GroupInvites
            .Where(i => i.GroupId == groupId && i.ClaimsMemberId == source.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(i => i.ClaimsMemberId, target.Id), ct);

        if (MergeDefaultSplitValues(group, source.Id, target.Id))
        {
            group.UpdatedAt = clock.UtcNow;
            await writer.RecordAsync(group, SyncEntityType.Group, groupId, SyncOperation.Update,
                deviceId, userId, GroupPayload(group), ct: ct);
        }

        // Nothing points at the source any more, so it goes rather than lingering
        // as a person with no history who cannot be told apart from a real one.
        source.Status = MembershipStatus.Removed;
        source.LeftAt = clock.UtcNow;
        await writer.RecordAsync(source, SyncEntityType.GroupMember, groupId, SyncOperation.Delete,
            deviceId, userId, MemberPayload(source), ct: ct);

        await activity.RecordAsync(groupId, ActivityKind.MembersMerged, userId, actor.Id,
            SyncEntityType.GroupMember, target.Id,
            $"{actor.DisplayName} merged {source.DisplayName} into {target.DisplayName} in {group.Name}",
            ct: ct);

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        return await GetAsync(userId, groupId, ct);
    }

    /// <summary>
    /// Moves the source's share of an expense onto the target, adding to whatever
    /// the target already had. Two shares of one expense held by what turns out to
    /// be one person are one share.
    /// </summary>
    private void MergeSplits(Expense expense, Guid sourceId, Guid targetId)
    {
        var mine = expense.Splits.FirstOrDefault(s => s.MemberId == sourceId);
        if (mine is null) return;

        var theirs = expense.Splits.FirstOrDefault(s => s.MemberId == targetId);

        if (theirs is null)
        {
            mine.MemberId = targetId;
            return;
        }

        theirs.Amount += mine.Amount;
        theirs.AmountInBaseCurrency += mine.AmountInBaseCurrency;

        // The input is what someone typed: two shares, two percentages or two
        // exact amounts all add up. Null means the split type does not take one.
        theirs.InputValue = mine.InputValue is null && theirs.InputValue is null
            ? null
            : (theirs.InputValue ?? 0m) + (mine.InputValue ?? 0m);

        expense.Splits.Remove(mine);
        db.ExpenseSplits.Remove(mine);
    }

    /// <summary>
    /// The same for an itemised line, where a share is membership rather than an
    /// amount: being on it twice is being on it once.
    /// </summary>
    private void MergeShares(ExpenseItem item, Guid sourceId, Guid targetId)
    {
        var mine = item.Shares.FirstOrDefault(s => s.MemberId == sourceId);
        if (mine is null) return;

        if (item.Shares.Any(s => s.MemberId == targetId))
        {
            item.Shares.Remove(mine);
            db.ExpenseItemShares.Remove(mine);
            return;
        }

        mine.MemberId = targetId;
    }

    /// <summary>
    /// Moves the source's weight in the group default onto the target. Returns
    /// whether anything changed, so the group is only re-recorded when it did.
    /// </summary>
    private static bool MergeDefaultSplitValues(Group group, Guid sourceId, Guid targetId)
    {
        var stored = ReadDefaultSplitValues(group.DefaultSplitValuesJson);
        if (stored is null || !stored.TryGetValue(sourceId, out var weight)) return false;

        var values = new Dictionary<Guid, decimal>(stored);
        values.Remove(sourceId);
        values[targetId] = values.TryGetValue(targetId, out var existing) ? existing + weight : weight;

        group.DefaultSplitValuesJson = values.Count == 0
            ? null
            : JsonSerializer.Serialize(values);

        return true;
    }

    private async Task<GroupMember> RequireMemberAsync(Guid groupId, Guid memberId, CancellationToken ct)
        => await db.GroupMembers.FirstOrDefaultAsync(m => m.Id == memberId && m.GroupId == groupId, ct)
           ?? throw new NotFoundException($"Member {memberId}");

    public async Task RemoveMemberAsync(Guid userId, Guid groupId, Guid memberId, CancellationToken ct = default)
    {
        var actor = await GroupAccess.RequireAdminAsync(db, userId, groupId, ct);
        var group = await GroupAccess.RequireGroupAsync(db, groupId, ct);
        GroupAccess.RequireWritable(group);

        var member = await db.GroupMembers
                         .FirstOrDefaultAsync(m => m.Id == memberId && m.GroupId == groupId, ct)
                     ?? throw new NotFoundException($"Member {memberId}");

        if (member.Role == GroupRole.Owner)
        {
            var otherOwners = await db.GroupMembers.CountAsync(m =>
                m.GroupId == groupId && m.Id != memberId
                && m.Role == GroupRole.Owner && m.Status == MembershipStatus.Active
                && !m.IsDeleted, ct);

            if (otherOwners == 0)
                throw new ValidationException("A group must keep at least one owner.");
        }

        var hasHistory = await db.Expenses.AnyAsync(e => e.PaidByMemberId == memberId, ct)
                         || await db.ExpenseSplits.AnyAsync(s => s.MemberId == memberId, ct)
                         || await db.Settlements.AnyAsync(s =>
                             s.FromMemberId == memberId || s.ToMemberId == memberId, ct);

        if (hasHistory)
        {
            // Deactivate rather than delete: their past expenses still need a payer,
            // and other members' balances depend on those rows.
            member.Status = MembershipStatus.Removed;
            member.LeftAt = clock.UtcNow;
            await writer.RecordAsync(member, SyncEntityType.GroupMember, groupId, SyncOperation.Update,
                DeviceFor(userId), userId, MemberPayload(member), ct: ct);
        }
        else
        {
            member.Status = MembershipStatus.Removed;
            member.LeftAt = clock.UtcNow;
            await writer.RecordAsync(member, SyncEntityType.GroupMember, groupId, SyncOperation.Delete,
                DeviceFor(userId), userId, MemberPayload(member), ct: ct);
        }

        await activity.RecordAsync(groupId, ActivityKind.MemberRemoved, userId, actor.Id,
            SyncEntityType.GroupMember, memberId,
            $"{actor.DisplayName} removed {member.DisplayName} from {group.Name}", ct: ct);

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    public async Task<IReadOnlyList<GroupLineageDto>> GetLineageAsync(
        Guid userId, Guid groupId, CancellationToken ct = default)
    {
        await GroupAccess.RequireMemberAsync(db, userId, groupId, ct);

        var links = await db.GroupLineageLinks
            .Where(l => l.SourceGroupId == groupId || l.TargetGroupId == groupId)
            .OrderBy(l => l.OccurredAt)
            .ToListAsync(ct);

        var groupIds = links.SelectMany(l => new[] { l.SourceGroupId, l.TargetGroupId }).Distinct().ToList();
        var names = await db.Groups
            .Where(g => groupIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.Name, ct);

        return links.Select(l => new GroupLineageDto(
            l.Id, l.Kind, l.SourceGroupId, names.GetValueOrDefault(l.SourceGroupId),
            l.TargetGroupId, names.GetValueOrDefault(l.TargetGroupId), l.OccurredAt, l.Note)).ToList();
    }

    /// <summary>
    /// Net position per member, in the group base currency. Shared by the group
    /// read, the list and the balance endpoint so they cannot disagree.
    /// </summary>
    internal async Task<Dictionary<Guid, decimal>> ComputeBalancesAsync(
        Guid groupId, IEnumerable<Guid> memberIds, string currency, CancellationToken ct)
    {
        var expenses = await db.Expenses
            .Where(e => e.GroupId == groupId && !e.IsDeleted)
            .Select(e => new
            {
                e.PaidByMemberId,
                e.AmountInBaseCurrency,
                Splits = e.Splits.Where(s => !s.IsDeleted)
                    .Select(s => new { s.MemberId, s.AmountInBaseCurrency })
                    .ToList()
            })
            .ToListAsync(ct);

        var settlements = await db.Settlements
            .Where(s => s.GroupId == groupId && !s.IsDeleted)
            .Select(s => new BalanceSettlement(s.FromMemberId, s.ToMemberId, s.AmountInBaseCurrency))
            .ToListAsync(ct);

        var balanceExpenses = expenses.Select(e => new BalanceExpense(
            e.PaidByMemberId, e.AmountInBaseCurrency,
            e.Splits.Select(s => (s.MemberId, s.AmountInBaseCurrency)).ToList())).ToList();

        return BalanceCalculator
            .NetBalances(memberIds, balanceExpenses, settlements, currency)
            .ToDictionary(b => b.MemberId, b => b.Net);
    }

    private GroupMember NewMember(Guid groupId, Guid? userId, string displayName, GroupRole role) => new()
    {
        GroupId = groupId,
        UserId = userId,
        DisplayName = displayName,
        Role = role,
        Status = MembershipStatus.Active,
        JoinedAt = clock.UtcNow,
        CreatedAt = clock.UtcNow,
        UpdatedAt = clock.UtcNow
    };

    /// <summary>
    /// Server-side writes still need a device id for the vector clock. A stable
    /// per-user pseudo device keeps API-originated changes causally ordered against
    /// the same user's real devices instead of inventing a new one every request.
    /// </summary>
    internal static string DeviceFor(Guid userId) => $"server:{userId:N}";

    internal static object GroupPayload(Group group) => new
    {
        group.Id, group.Name, group.Description, group.BaseCurrency,
        group.IconName, group.ColorHex, group.IsArchived, group.LineageId,
        // In the payload so another device learns the group's default split from
        // the delta pull rather than only on a full read.
        DefaultSplitType = (int)group.DefaultSplitType,
        group.DefaultSplitValuesJson,
        group.IsDeleted
    };

    internal static object MemberPayload(GroupMember member) => new
    {
        member.Id, member.GroupId, member.UserId, member.DisplayName,
        Role = (int)member.Role, Status = (int)member.Status, member.IsDeleted
    };
}
