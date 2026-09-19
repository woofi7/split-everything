using SplitEverything.Application.Contracts.Groups;

namespace SplitEverything.Application.Services;

public interface IGroupService
{
    Task<GroupDto> CreateAsync(Guid userId, CreateGroupRequest request, CancellationToken ct = default);
    Task<GroupDto> GetAsync(Guid userId, Guid groupId, CancellationToken ct = default);
    Task<IReadOnlyList<GroupSummaryDto>> ListAsync(Guid userId, bool includeArchived = false, CancellationToken ct = default);
    Task<GroupDto> UpdateAsync(Guid userId, Guid groupId, UpdateGroupRequest request, CancellationToken ct = default);

    Task<GroupDto> SetIgnoredNamesAsync(Guid userId, Guid groupId, SetIgnoredNamesRequest request, CancellationToken ct = default);

    Task<GroupDto> ArchiveAsync(Guid userId, Guid groupId, CancellationToken ct = default);
    Task<GroupDto> UnarchiveAsync(Guid userId, Guid groupId, CancellationToken ct = default);

    Task<GroupMemberDto> AddUserMemberAsync(Guid userId, Guid groupId, AddUserMemberRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<AddableUserDto>> ListAddableUsersAsync(Guid userId, Guid? groupId, CancellationToken ct = default);

    Task<GroupMemberDto> SetMemberColorAsync(Guid userId, Guid groupId, Guid memberId, SetMemberColorRequest request, CancellationToken ct = default);
    Task<GroupDto> MergeMembersAsync(Guid userId, Guid groupId, MergeMembersRequest request, CancellationToken ct = default);
    Task RemoveMemberAsync(Guid userId, Guid groupId, Guid memberId, CancellationToken ct = default);

    Task<IReadOnlyList<GroupLineageDto>> GetLineageAsync(Guid userId, Guid groupId, CancellationToken ct = default);
}
