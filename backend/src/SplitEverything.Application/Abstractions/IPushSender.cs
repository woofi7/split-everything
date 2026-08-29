using SplitEverything.Domain.Common;

namespace SplitEverything.Application.Abstractions;

public sealed record PushMessage(
    string Title,
    string Body,
    string? Url = null,
    string? Tag = null,
    IReadOnlyDictionary<string, string>? Data = null);

public sealed record PushTarget(
    Guid SubscriptionId,
    PushChannel Channel,
    string Endpoint,
    string? P256dh,
    string? Auth);

/// <summary>
/// One channel of delivery. Native APNs/FCM is primary for the Capacitor shells,
/// Web Push is the browser fallback; the dispatcher fans out to whichever
/// subscriptions a user has registered.
/// </summary>
public interface IPushSender
{
    PushChannel Channel { get; }

    /// <summary>Returns false when the subscription is gone and should be pruned.</summary>
    Task<bool> SendAsync(PushTarget target, PushMessage message, CancellationToken ct = default);
}

public interface IPushDispatcher
{
    Task SendToUsersAsync(IReadOnlyCollection<Guid> userIds, PushMessage message, CancellationToken ct = default);
    Task SendToGroupAsync(Guid groupId, PushMessage message, Guid? exceptUserId = null, CancellationToken ct = default);
}
