using SplitEverything.Domain.Common;

namespace SplitEverything.Domain.Entities;

public class GroupMember : SyncableEntity
{
    public Guid GroupId { get; set; }
    public Group? Group { get; set; }

    /// <summary>
    /// Null for a placeholder member: someone who exists in the group's history
    /// (typically created by a Settle Up import, which only carries names) but has
    /// not signed in yet. Claimed when they accept an invite.
    /// </summary>
    public Guid? UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Name shown in this group. Defaults to the user's display name.</summary>
    public string DisplayName { get; set; } = string.Empty;

    public GroupRole Role { get; set; } = GroupRole.Member;
    public MembershipStatus Status { get; set; } = MembershipStatus.Active;

    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LeftAt { get; set; }

    public bool IsPlaceholder => UserId is null;
}
