using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SplitEverything.Application.Abstractions;
using SplitEverything.Domain.Common;
using SplitEverything.Infrastructure.Persistence;

namespace SplitEverything.Infrastructure.Notifications;

public sealed class PushDispatcher(
    AppDbContext db,
    IEnumerable<IPushSender> senders,
    IClock clock,
    ILogger<PushDispatcher> logger) : IPushDispatcher
{
    public async Task SendToUsersAsync(
        IReadOnlyCollection<Guid> userIds, PushMessage message, CancellationToken ct = default)
    {
        if (userIds.Count == 0) return;

        var subscriptions = await db.PushSubscriptions
            .Where(p => userIds.Contains(p.UserId))
            .ToListAsync(ct);

        if (subscriptions.Count == 0) return;

        var byChannel = senders.ToDictionary(s => s.Channel);
        var dead = new List<Guid>();

        foreach (var subscription in subscriptions)
        {
            if (!byChannel.TryGetValue(subscription.Channel, out var sender))
            {
                logger.LogDebug("No sender registered for {Channel}", subscription.Channel);
                continue;
            }

            var target = new PushTarget(subscription.Id, subscription.Channel,
                subscription.Endpoint, subscription.P256dh, subscription.Auth);

            try
            {
                var delivered = await sender.SendAsync(target, message, ct);

                if (delivered)
                {
                    subscription.LastUsedAt = clock.UtcNow;
                    subscription.FailingSince = null;
                }
                else
                {
                    dead.Add(subscription.Id);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Push to {Channel} failed", subscription.Channel);
                subscription.FailingSince ??= clock.UtcNow;
            }
        }

        if (dead.Count > 0)
        {
            await db.PushSubscriptions
                .Where(p => dead.Contains(p.Id))
                .ExecuteDeleteAsync(ct);
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    public async Task SendToGroupAsync(
        Guid groupId, PushMessage message, Guid? exceptUserId = null, CancellationToken ct = default)
    {
        var userIds = await db.GroupMembers
            .Where(m => m.GroupId == groupId
                        && m.UserId != null
                        && m.Status == MembershipStatus.Active
                        && !m.IsDeleted
                        && (exceptUserId == null || m.UserId != exceptUserId))
            .Select(m => m.UserId!.Value)
            .Distinct()
            .ToListAsync(ct);

        await SendToUsersAsync(userIds, message, ct);
    }
}
