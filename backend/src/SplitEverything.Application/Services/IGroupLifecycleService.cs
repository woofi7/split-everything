using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Application.Contracts.Sync;

namespace SplitEverything.Application.Services;

public interface IGroupLifecycleService
{
    Task<MergeGroupsResult> MergeAsync(Guid userId, MergeGroupsRequest request, CancellationToken ct = default);
    Task<SplitGroupResult> SplitAsync(Guid userId, SplitGroupRequest request, CancellationToken ct = default);

    Task<TransferExpenseResult> TransferExpenseAsync(Guid userId, TransferExpenseRequest request, CancellationToken ct = default);

    Task<CompactionResult> CompactAsync(Guid groupId, DateTimeOffset cutoff, CancellationToken ct = default);
}
