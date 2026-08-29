using Microsoft.Extensions.Logging;
using SplitEverything.Application.Abstractions;
using SplitEverything.Domain.Common;
using WebPush;

namespace SplitEverything.Infrastructure.Notifications;

/// <summary>
/// Browser Web Push over VAPID. The fallback channel for anyone using the PWA
/// rather than the Capacitor shells.
/// </summary>
public sealed class WebPushSender(PushOptions options, ILogger<WebPushSender> logger) : IPushSender
{
    public PushChannel Channel => PushChannel.WebPush;

    public async Task<bool> SendAsync(
        PushTarget target, PushMessage message, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.VapidPublicKey) || string.IsNullOrWhiteSpace(options.VapidPrivateKey))
        {
            logger.LogDebug("Web Push is not configured; skipping");
            return true;
        }

        if (string.IsNullOrWhiteSpace(target.P256dh) || string.IsNullOrWhiteSpace(target.Auth))
        {
            // Without the keys the subscription can never be encrypted to; prune it.
            logger.LogWarning("Dropping a Web Push subscription with no encryption keys");
            return false;
        }

        var client = new WebPushClient();
        var subscription = new WebPush.PushSubscription(target.Endpoint, target.P256dh, target.Auth);
        var vapid = new VapidDetails(options.VapidSubject, options.VapidPublicKey, options.VapidPrivateKey);

        try
        {
            await client.SendNotificationAsync(subscription, PushPayload.Serialize(message), vapid, ct);
            return true;
        }
        catch (WebPushException ex)
        {
            // 404 and 410 mean the browser dropped the subscription for good.
            var gone = ex.StatusCode is System.Net.HttpStatusCode.NotFound
                or System.Net.HttpStatusCode.Gone;

            logger.Log(gone ? LogLevel.Information : LogLevel.Warning, ex,
                "Web Push to {Endpoint} failed with {Status}", target.Endpoint, ex.StatusCode);

            return !gone;
        }
    }
}
