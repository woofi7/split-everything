using SplitEverything.Application.Contracts.Settlements;

namespace SplitEverything.Application.Services;

public interface ISettlementService
{
    Task<SettlementDto> CreateAsync(Guid userId, CreateSettlementRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<SettlementDto>> ListAsync(Guid userId, Guid groupId, CancellationToken ct = default);
    Task DeleteAsync(Guid userId, Guid settlementId, CancellationToken ct = default);

    /// <summary>Net positions plus both the simplified plan and the raw pairwise view.</summary>
    Task<GroupBalanceDto> GetGroupBalanceAsync(Guid userId, Guid groupId, CancellationToken ct = default);

    /// <summary>Net position across every group, converted into the user's own currency.</summary>
    Task<OverallBalanceDto> GetOverallBalanceAsync(Guid userId, CancellationToken ct = default);

    Task NudgeAsync(Guid userId, NudgeRequest request, CancellationToken ct = default);
}
