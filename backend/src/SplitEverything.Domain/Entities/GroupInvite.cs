namespace SplitEverything.Domain.Entities;

public class GroupInvite
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid GroupId { get; set; }
    public Group? Group { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public string? InvitedEmail { get; set; }

    public Guid? ClaimsMemberId { get; set; }

    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }

    public int MaxUses { get; set; } = 1;
    public int UseCount { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsRedeemableAt(DateTimeOffset now)
        => RevokedAt is null && UseCount < MaxUses && ExpiresAt > now;
}
