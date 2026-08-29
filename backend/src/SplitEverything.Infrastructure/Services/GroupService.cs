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
            EmojiIcon = request.EmojiIcon,
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
            group.EmojiIcon, group.ColorHex, group.IsArchived,
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
            totals?.Count ?? 0);
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
                m.Group.EmojiIcon,
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
                row.GroupId, row.Name, row.BaseCurrency, row.EmojiIcon, row.ColorHex,
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
        if (request.Description is not null)
            group.Description = request.Description.Trim();
        if (request.EmojiIcon is not null)
            group.EmojiIcon = request.EmojiIcon.Trim();
        if (request.ColorHex is not null)
            group.ColorHex = request.ColorHex.Trim();
        if (request.BaseCurrency is not null)
            group.BaseCurrency = GroupAccess.NormalizeCurrency(request.BaseCurrency, "Base currency");

        await writer.RecordAsync(group, SyncEntityType.Group, groupId, SyncOperation.Update,
            DeviceFor(userId), userId, GroupPayload(group), ct: ct);

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        return await GetAsync(userId, groupId, ct);
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

    public async Task<GroupMemberDto> AddPlaceholderMemberAsync(
        Guid userId, Guid groupId, AddPlaceholderMemberRequest request, CancellationToken ct = default)
    {
        var actor = await GroupAccess.RequireMemberAsync(db, userId, groupId, ct);
        var group = await GroupAccess.RequireGroupAsync(db, groupId, ct);
        GroupAccess.RequireWritable(group);

        var displayName = GroupAccess.RequireText(request.DisplayName, "Display name", 120);

        var member = NewMember(groupId, null, displayName, GroupRole.Member);
        db.GroupMembers.Add(member);

        await writer.RecordAsync(member, SyncEntityType.GroupMember, groupId, SyncOperation.Create,
            DeviceFor(userId), userId, MemberPayload(member), ct: ct);

        await activity.RecordAsync(groupId, ActivityKind.MemberInvited, userId, actor.Id,
            SyncEntityType.GroupMember, member.Id,
            $"{actor.DisplayName} added {displayName} to {group.Name}", ct: ct);

        await db.SaveChangesAsync(ct);

        return new GroupMemberDto(member.Id, null, member.DisplayName, null,
            member.Role, member.Status, true, 0m);
    }

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
        group.EmojiIcon, group.ColorHex, group.IsArchived, group.LineageId,
        group.IsDeleted
    };

    internal static object MemberPayload(GroupMember member) => new
    {
        member.Id, member.GroupId, member.UserId, member.DisplayName,
        Role = (int)member.Role, Status = (int)member.Status, member.IsDeleted
    };
}
