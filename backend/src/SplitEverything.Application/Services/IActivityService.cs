using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Notifications;
using SplitEverything.Domain.Common;

namespace SplitEverything.Application.Services;

public interface IActivityService
{
    Task<Paged<ActivityEntryDto>> ListAsync(Guid userId, Guid? groupId, PageRequest page, CancellationToken ct = default);

    Task RecordAsync(
        Guid? groupId, ActivityKind kind, Guid? actorUserId, Guid? actorMemberId,
        SyncEntityType? subjectType, Guid? subjectId, string summary, object? metadata = null,
        CancellationToken ct = default);
}
