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
    /// The colour this person would like to be, in the groups they join.
    ///
    /// A wish rather than a guarantee: a group where somebody already has it gives
    /// them the next free one, because two people the same colour in one group
    /// defeats the whole purpose of having colours.
    /// </summary>
    public string? PreferredColorHex { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSeenAt { get; set; }

    public ICollection<GroupMember> Memberships { get; set; } = new List<GroupMember>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public ICollection<PushSubscription> PushSubscriptions { get; set; } = new List<PushSubscription>();
    public ICollection<Device> Devices { get; set; } = new List<Device>();
}
