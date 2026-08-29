namespace SplitEverything.Infrastructure.Notifications;

public sealed class PushOptions
{
    public const string SectionName = "Push";

    /// <summary>VAPID keys for browser Web Push. Public key is served to the client.</summary>
    public string VapidPublicKey { get; set; } = string.Empty;
    public string VapidPrivateKey { get; set; } = string.Empty;
    public string VapidSubject { get; set; } = "mailto:admin@example.com";

    /// <summary>FCM HTTP v1 is used for Android through the Capacitor shell.</summary>
    public string? FcmProjectId { get; set; }
    public string? FcmServiceAccountJson { get; set; }

    /// <summary>APNs token-based auth for the iOS shell.</summary>
    public string? ApnsKeyId { get; set; }
    public string? ApnsTeamId { get; set; }
    public string? ApnsBundleId { get; set; }
    public string? ApnsPrivateKey { get; set; }
    public bool ApnsUseSandbox { get; set; }
}
