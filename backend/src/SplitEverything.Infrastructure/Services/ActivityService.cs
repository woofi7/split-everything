using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Notifications;
using SplitEverything.Application.Services;
using SplitEverything.Domain.Common;
using SplitEverything.Domain.Entities;
using SplitEverything.Infrastructure.Persistence;

namespace SplitEverything.Infrastructure.Services;

public sealed class ActivityService(AppDbContext db, IClock clock) : IActivityService
{
    public async Task<Paged<ActivityEntryDto>> ListAsync(
        Guid userId, Guid? groupId, PageRequest page, CancellationToken ct = default)
    {
        // Scope to the groups the caller is actually in, so the feed can never leak
        // activity from a group they were removed from.
        var visibleGroupIds = db.GroupMembers
            .Where(m => m.UserId == userId && m.Status == MembershipStatus.Active && !m.IsDeleted)
            .Select(m => m.GroupId);

        if (groupId is not null)
        {
            await GroupAccess.RequireMemberAsync(db, userId, groupId.Value, ct);
            visibleGroupIds = visibleGroupIds.Where(id => id == groupId.Value);
        }

        var query = db.ActivityLog
            .Where(a => a.GroupId != null && visibleGroupIds.Contains(a.GroupId.Value))
            .OrderByDescending(a => a.OccurredAt)
            .ThenByDescending(a => a.Id);

        var total = await query.CountAsync(ct);

        var rows = await query
            .Skip(page.Skip)
            .Take(page.Clamped)
            .Select(a => new
            {
                Entry = a,
                GroupName = a.Group!.Name,
                ActorName = db.Users.Where(u => u.Id == a.ActorUserId).Select(u => u.DisplayName).FirstOrDefault(),
                ActorAvatar = db.Users.Where(u => u.Id == a.ActorUserId).Select(u => u.AvatarUrl).FirstOrDefault()
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new ActivityEntryDto(
            r.Entry.Id, r.Entry.GroupId, r.GroupName, r.Entry.Kind,
            r.Entry.ActorUserId, r.ActorName, r.ActorAvatar,
            r.Entry.SubjectType, r.Entry.SubjectId,
            r.Entry.Summary, r.Entry.MetadataJson, r.Entry.OccurredAt)).ToList();

        return new Paged<ActivityEntryDto>(items, total, page.Page, page.Clamped);
    }

    public Task RecordAsync(
        Guid? groupId, ActivityKind kind, Guid? actorUserId, Guid? actorMemberId,
        SyncEntityType? subjectType, Guid? subjectId, string summary, object? metadata = null,
        CancellationToken ct = default)
    {
        db.ActivityLog.Add(new ActivityLogEntry
        {
            GroupId = groupId,
            Kind = kind,
            ActorUserId = actorUserId,
            ActorMemberId = actorMemberId,
            SubjectType = subjectType,
            SubjectId = subjectId,
            Summary = summary.Length > 500 ? summary[..500] : summary,
            MetadataJson = metadata is null ? null : JsonSerializer.Serialize(metadata),
            OccurredAt = clock.UtcNow
        });

        // Not saved here: the caller owns the transaction, so the feed entry lands
        // with the change it describes or not at all.
        return Task.CompletedTask;
    }
}
