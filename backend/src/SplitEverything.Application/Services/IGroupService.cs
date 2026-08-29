using SplitEverything.Application.Contracts.Groups;

namespace SplitEverything.Application.Services;

public interface IGroupService
{
    Task<GroupDto> CreateAsync(Guid userId, CreateGroupRequest request, CancellationToken ct = default);
    Task<GroupDto> GetAsync(Guid userId, Guid groupId, CancellationToken ct = default);
    Task<IReadOnlyList<GroupSummaryDto>> ListAsync(Guid userId, bool includeArchived = false, CancellationToken ct = default);
    Task<GroupDto> UpdateAsync(Guid userId, Guid groupId, UpdateGroupRequest request, CancellationToken ct = default);

    Task<GroupDto> ArchiveAsync(Guid userId, Guid groupId, CancellationToken ct = default);
    Task<GroupDto> UnarchiveAsync(Guid userId, Guid groupId, CancellationToken ct = default);

    Task<GroupMemberDto> AddPlaceholderMemberAsync(Guid userId, Guid groupId, AddPlaceholderMemberRequest request, CancellationToken ct = default);

    /// <summary>
    /// Adds someone who already has an account. The other way into a group is an
    /// invite link, which suits a person who has never opened the app; this is for
    /// one who is already here.
    /// </summary>
    Task<GroupMemberDto> AddUserMemberAsync(Guid userId, Guid groupId, AddUserMemberRequest request, CancellationToken ct = default);

    /// <summary>
    /// People with an account who could be added to a group: everyone but the
    /// caller and the group's current members. Pass a null group for a group that
    /// does not exist yet.
    ///
    /// Every account on the instance, deliberately, not only people the caller
    /// already shares a group with. The point of this list is to add someone who
    /// has just signed up, and they share nothing yet; narrowing it would leave
    /// that case with no route in but an invite link. The spec scopes this app to
    /// the owner and the people they invited, so an account existing already means
    /// the owner let that person in. Confirmed as intended, so it is not a leak to
    /// be tightened later without changing that scope first.
    /// </summary>
    Task<IReadOnlyList<AddableUserDto>> ListAddableUsersAsync(Guid userId, Guid? groupId, CancellationToken ct = default);

    /// <summary>
    /// Removes a member. Someone with history is deactivated rather than deleted, so
    /// past expenses keep pointing at a real row.
    /// </summary>
    Task RemoveMemberAsync(Guid userId, Guid groupId, Guid memberId, CancellationToken ct = default);

    Task<IReadOnlyList<GroupLineageDto>> GetLineageAsync(Guid userId, Guid groupId, CancellationToken ct = default);
}
