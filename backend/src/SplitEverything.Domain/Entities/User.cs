namespace SplitEverything.Domain.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string GoogleSubject { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }

    public string DefaultCurrency { get; set; } = "CAD";
    public string Locale { get; set; } = "en";
    public bool PrefersLightTheme { get; set; }

    public string? ThemeName { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSeenAt { get; set; }

    public ICollection<GroupMember> Memberships { get; set; } = new List<GroupMember>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public ICollection<PushSubscription> PushSubscriptions { get; set; } = new List<PushSubscription>();
    public ICollection<Device> Devices { get; set; } = new List<Device>();
}
