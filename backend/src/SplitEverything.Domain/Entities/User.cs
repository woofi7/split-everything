namespace SplitEverything.Domain.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Google's stable subject claim. The only identity we trust.</summary>
    public string GoogleSubject { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }

    /// <summary>Currency used for the cross-group net balance view.</summary>
    public string DefaultCurrency { get; set; } = "CAD";
    public string Locale { get; set; } = "en";
    public bool PrefersLightTheme { get; set; }

    /// <summary>
    /// The accent colour this person wants the whole application to wear.
    ///
    /// A name from <see cref="Common.AppThemes"/> rather than a colour, because a
    /// theme is three shades and the client is what knows them. Null means they
    /// have not said, which is the default theme.
    ///
    /// On the account rather than on the device: someone who picks a colour means
    /// it wherever they sign in.
    /// </summary>
    public string? ThemeName { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSeenAt { get; set; }

    public ICollection<GroupMember> Memberships { get; set; } = new List<GroupMember>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public ICollection<PushSubscription> PushSubscriptions { get; set; } = new List<PushSubscription>();
    public ICollection<Device> Devices { get; set; } = new List<Device>();
}
