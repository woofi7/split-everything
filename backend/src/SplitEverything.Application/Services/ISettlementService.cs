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

    /// <summary>What the caller and one other person owe each other in every group they share.</summary>
    Task<CrossGroupBalanceDto> GetCrossGroupBalanceAsync(
        Guid userId, Guid withUserId, CancellationToken ct = default);

    /// <summary>
    /// Cancels the debts that face each other across two groups, by writing a
    /// settlement in each. Nothing is paid: the pair exists so that the balance
    /// left over sits in one place rather than in two that disagree.
    /// </summary>
    Task<OffsetAcrossGroupsResult> OffsetAcrossGroupsAsync(
        Guid userId, OffsetAcrossGroupsRequest request, CancellationToken ct = default);
}
