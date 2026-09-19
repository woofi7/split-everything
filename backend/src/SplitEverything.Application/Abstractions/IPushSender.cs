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

public interface IPushSender
{
    PushChannel Channel { get; }

    Task<bool> SendAsync(PushTarget target, PushMessage message, CancellationToken ct = default);
}

public interface IPushDispatcher
{
    Task SendToUsersAsync(IReadOnlyCollection<Guid> userIds, PushMessage message, CancellationToken ct = default);
    Task SendToGroupAsync(Guid groupId, PushMessage message, Guid? exceptUserId = null, CancellationToken ct = default);
}
