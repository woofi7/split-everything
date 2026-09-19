namespace SplitEverything.Infrastructure.Notifications;

public sealed class PushOptions
{
    public const string SectionName = "Push";

    public string VapidPublicKey { get; set; } = string.Empty;
    public string VapidPrivateKey { get; set; } = string.Empty;
    public string VapidSubject { get; set; } = "mailto:admin@example.com";

    public string? FcmProjectId { get; set; }
    public string? FcmServiceAccountJson { get; set; }

    public string? ApnsKeyId { get; set; }
    public string? ApnsTeamId { get; set; }
    public string? ApnsBundleId { get; set; }
    public string? ApnsPrivateKey { get; set; }
    public bool ApnsUseSandbox { get; set; }
}
