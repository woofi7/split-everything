using SplitEverything.Application.Contracts.Settlements;

namespace SplitEverything.Application.Services;

public interface ISettlementService
{
    Task<SettlementDto> CreateAsync(Guid userId, CreateSettlementRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<SettlementDto>> ListAsync(Guid userId, Guid groupId, CancellationToken ct = default);
    Task DeleteAsync(Guid userId, Guid settlementId, CancellationToken ct = default);

    Task<GroupBalanceDto> GetGroupBalanceAsync(Guid userId, Guid groupId, CancellationToken ct = default);

    Task<OverallBalanceDto> GetOverallBalanceAsync(Guid userId, CancellationToken ct = default);

    Task NudgeAsync(Guid userId, NudgeRequest request, CancellationToken ct = default);

    Task<CrossGroupBalanceDto> GetCrossGroupBalanceAsync(
        Guid userId, Guid withUserId, CancellationToken ct = default);

    Task<OffsetAcrossGroupsResult> OffsetAcrossGroupsAsync(
        Guid userId, OffsetAcrossGroupsRequest request, CancellationToken ct = default);
}
