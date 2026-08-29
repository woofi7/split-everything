using SplitEverything.Domain.Common;

namespace SplitEverything.Application.Contracts.Notifications;

public sealed record RegisterPushRequest(
    PushChannel Channel,
    string Endpoint,
    string? P256dh,
    string? Auth,
    string? DeviceId);

public sealed record PushSubscriptionDto(Guid Id, PushChannel Channel, string Endpoint, string? DeviceId, DateTimeOffset CreatedAt);

public sealed record VapidPublicKeyDto(string PublicKey);

public sealed record ActivityEntryDto(
    long Id,
    Guid? GroupId,
    string? GroupName,
    ActivityKind Kind,
    Guid? ActorUserId,
    string? ActorName,
    string? ActorAvatarUrl,
    SyncEntityType? SubjectType,
    Guid? SubjectId,
    string Summary,
    string? MetadataJson,
    DateTimeOffset OccurredAt);
