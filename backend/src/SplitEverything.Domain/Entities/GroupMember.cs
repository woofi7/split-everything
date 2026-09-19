using SplitEverything.Domain.Common;

namespace SplitEverything.Domain.Entities;

public class GroupMember : SyncableEntity
{
    public Guid GroupId { get; set; }
    public Group? Group { get; set; }

    public Guid? UserId { get; set; }
    public User? User { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public GroupRole Role { get; set; } = GroupRole.Member;
    public MembershipStatus Status { get; set; } = MembershipStatus.Active;

    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LeftAt { get; set; }

    public bool IsPlaceholder => UserId is null;

    public string? ColorHex { get; set; }
}
