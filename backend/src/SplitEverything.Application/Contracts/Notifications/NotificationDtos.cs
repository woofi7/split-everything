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
    // The membership, not just the account. The app colours people by member id,
    // so without this the same person would get one colour on an expense card and
    // another in the feed.
    Guid? ActorMemberId,
    string? ActorName,
    string? ActorAvatarUrl,
    SyncEntityType? SubjectType,
    Guid? SubjectId,
    string Summary,
    string? MetadataJson,
    DateTimeOffset OccurredAt);
