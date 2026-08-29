namespace SplitEverything.Domain.Entities;

/// <summary>
/// A magic link. Rendered either as an emailed URL or as a QR code - same token,
/// two presentations. Redeeming it requires a Google sign-in, so the link alone
/// never grants access.
/// </summary>
public class GroupInvite
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid GroupId { get; set; }
    public Group? Group { get; set; }

    /// <summary>SHA-256 of the token. The plaintext exists only in the link we hand out.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Optional: pins the invite to one address, so a leaked link is useless to anyone else.</summary>
    public string? InvitedEmail { get; set; }

    /// <summary>Optional: the placeholder member this invite claims on redemption.</summary>
    public Guid? ClaimsMemberId { get; set; }

    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }

    public int MaxUses { get; set; } = 1;
    public int UseCount { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsRedeemable => RevokedAt is null
        && UseCount < MaxUses
        && ExpiresAt > DateTimeOffset.UtcNow;
}
