using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Expenses;

namespace SplitEverything.Application.Services;

public interface IExpenseService
{
    Task<ExpenseDto> CreateAsync(Guid userId, CreateExpenseRequest request, CancellationToken ct = default);
    Task<ExpenseDto> GetAsync(Guid userId, Guid expenseId, CancellationToken ct = default);
    Task<Paged<ExpenseDto>> ListAsync(Guid userId, ExpenseQuery query, CancellationToken ct = default);
    Task<ExpenseDto> UpdateAsync(Guid userId, Guid expenseId, UpdateExpenseRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid userId, Guid expenseId, CancellationToken ct = default);

    Task<IReadOnlyList<ExpenseRevisionDto>> GetHistoryAsync(Guid userId, Guid expenseId, CancellationToken ct = default);

    Task<CommentDto> AddCommentAsync(Guid userId, CreateCommentRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<CommentDto>> GetCommentsAsync(Guid userId, Guid expenseId, CancellationToken ct = default);
    Task DeleteCommentAsync(Guid userId, Guid commentId, CancellationToken ct = default);
}
