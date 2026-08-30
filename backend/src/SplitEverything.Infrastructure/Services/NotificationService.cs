using Microsoft.EntityFrameworkCore;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Notifications;
using SplitEverything.Application.Services;
using SplitEverything.Domain.Common;
using SplitEverything.Infrastructure.Notifications;
using SplitEverything.Infrastructure.Persistence;

namespace SplitEverything.Infrastructure.Services;

public sealed class NotificationService(
    AppDbContext db,
    PushOptions options,
    IClock clock) : INotificationService
{
    public async Task<PushSubscriptionDto> RegisterAsync(
        Guid userId, RegisterPushRequest request, CancellationToken ct = default)
    {
        var endpoint = GroupAccess.RequireText(request.Endpoint, "Endpoint", 2048);

        if (request.Channel == PushChannel.WebPush
            && (string.IsNullOrWhiteSpace(request.P256dh) || string.IsNullOrWhiteSpace(request.Auth)))
        {
            // Web Push payloads are encrypted to these keys; without them the
            // subscription can never receive anything.
            throw new ValidationException("A Web Push subscription needs its p256dh and auth keys.");
        }

        var existing = await db.PushSubscriptions.FirstOrDefaultAsync(p => p.Endpoint == endpoint, ct);

        if (existing is not null)
        {
            if (existing.UserId != userId)
                throw new ForbiddenException("That endpoint is registered to another account.");

            existing.Channel = request.Channel;
            existing.P256dh = request.P256dh;
            existing.Auth = request.Auth;
            existing.DeviceId = request.DeviceId;
            existing.FailingSince = null;
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            return Map(existing);
        }

        var subscription = new Domain.Entities.PushSubscription
        {
            UserId = userId,
            Channel = request.Channel,
            Endpoint = endpoint,
            P256dh = request.P256dh,
            Auth = request.Auth,
            DeviceId = request.DeviceId,
            CreatedAt = clock.UtcNow
        };
        db.PushSubscriptions.Add(subscription);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        return Map(subscription);
    }

    public async Task UnregisterAsync(Guid userId, string endpoint, CancellationToken ct = default)
    {
        var subscription = await db.PushSubscriptions.FirstOrDefaultAsync(p => p.Endpoint == endpoint, ct);
        if (subscription is null) return;

        if (subscription.UserId != userId)
            throw new ForbiddenException("That endpoint is registered to another account.");

        db.PushSubscriptions.Remove(subscription);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    public async Task<IReadOnlyList<PushSubscriptionDto>> ListAsync(
        Guid userId, CancellationToken ct = default)
        => await db.PushSubscriptions
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.CreatedAt)
            .Select(p => new PushSubscriptionDto(p.Id, p.Channel, p.Endpoint, p.DeviceId, p.CreatedAt))
            .ToListAsync(ct);

    /// <summary>
    /// The public key, or nothing when what is configured is not one.
    ///
    /// Serving a malformed value is worse than serving none: the browser gets as far
    /// as decoding it and fails there, so the message names atob rather than the
    /// setting. Nothing is an answer the app already knows how to explain.
    /// </summary>
    public VapidPublicKeyDto GetVapidPublicKey() =>
        new(VapidKey.IsValidPublicKey(options.VapidPublicKey) ? options.VapidPublicKey : string.Empty);

    private static PushSubscriptionDto Map(Domain.Entities.PushSubscription subscription)
        => new(subscription.Id, subscription.Channel, subscription.Endpoint,
            subscription.DeviceId, subscription.CreatedAt);
}
