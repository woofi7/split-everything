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
    /// Removes a member. Someone with history is deactivated rather than deleted, so
    /// past expenses keep pointing at a real row.
    /// </summary>
    Task RemoveMemberAsync(Guid userId, Guid groupId, Guid memberId, CancellationToken ct = default);

    Task<IReadOnlyList<GroupLineageDto>> GetLineageAsync(Guid userId, Guid groupId, CancellationToken ct = default);
}
