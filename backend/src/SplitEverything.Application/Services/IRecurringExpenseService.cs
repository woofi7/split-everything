using SplitEverything.Application.Contracts.Expenses;

namespace SplitEverything.Application.Services;

public interface IRecurringExpenseService
{
    Task<RecurringExpenseDto> CreateAsync(Guid userId, CreateRecurringExpenseRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<RecurringExpenseDto>> ListAsync(Guid userId, Guid groupId, CancellationToken ct = default);
    Task<RecurringExpenseDto> SetPausedAsync(Guid userId, Guid id, bool paused, CancellationToken ct = default);
    Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default);

    /// <summary>
    /// Materialises every occurrence due at or before <paramref name="asOf"/>,
    /// backfilling any the app missed while it was down. Idempotent.
    /// </summary>
    Task<int> RunDueAsync(DateTimeOffset asOf, CancellationToken ct = default);
}
