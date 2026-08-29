using SplitEverything.Domain.Common;

namespace SplitEverything.Domain.Entities;

public class PushSubscription
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public PushChannel Channel { get; set; }

    /// <summary>Web Push endpoint URL, or the APNs/FCM device token.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Web Push only.</summary>
    public string? P256dh { get; set; }
    public string? Auth { get; set; }

    public string? DeviceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? FailingSince { get; set; }
}
