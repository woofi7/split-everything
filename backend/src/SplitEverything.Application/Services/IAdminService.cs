using SplitEverything.Application.Contracts.Admin;

namespace SplitEverything.Application.Services;

public interface IAdminService
{
    Task<bool> IsAdminAsync(Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<AdminGroupDto>> GetGroupsAsync(Guid userId, CancellationToken ct = default);

    Task<AdminGroupDetailDto> GetGroupAsync(Guid userId, Guid groupId, CancellationToken ct = default);

    Task DeleteGroupAsync(Guid userId, Guid groupId, CancellationToken ct = default);
}
