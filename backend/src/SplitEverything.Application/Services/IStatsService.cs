using SplitEverything.Application.Contracts.Stats;

namespace SplitEverything.Application.Services;

public interface IStatsService
{
    Task<StatsDashboardDto> GetDashboardAsync(Guid userId, StatsQuery query, CancellationToken ct = default);
}
