using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Application.Contracts.Sync;

namespace SplitEverything.Application.Services;

/// <summary>
/// Merge, split, transfer and compaction: the operations that rewrite which log an
/// entity's history lives in without breaking causality.
/// </summary>
public interface IGroupLifecycleService
{
    Task<MergeGroupsResult> MergeAsync(Guid userId, MergeGroupsRequest request, CancellationToken ct = default);
    Task<SplitGroupResult> SplitAsync(Guid userId, SplitGroupRequest request, CancellationToken ct = default);

    /// <summary>Moves one expense between groups with its revisions, comments and log entries intact.</summary>
    Task<TransferExpenseResult> TransferExpenseAsync(Guid userId, TransferExpenseRequest request, CancellationToken ct = default);

    /// <summary>Collapses settled history older than the cutoff into a snapshot and trims the live log.</summary>
    Task<CompactionResult> CompactAsync(Guid groupId, DateTimeOffset cutoff, CancellationToken ct = default);
}
