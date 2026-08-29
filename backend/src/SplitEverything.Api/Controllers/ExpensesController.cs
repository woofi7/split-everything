using Microsoft.AspNetCore.Mvc;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Expenses;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Application.Services;

namespace SplitEverything.Api.Controllers;

public sealed class ExpensesController(
    ICurrentUser currentUser,
    IExpenseService expenses,
    IRecurringExpenseService recurring,
    IGroupLifecycleService lifecycle) : ApiControllerBase(currentUser)
{
    [HttpGet]
    public async Task<ActionResult<Paged<ExpenseDto>>> List(
        [FromQuery] ExpenseQuery query, CancellationToken ct)
        => Ok(await expenses.ListAsync(UserId, query, ct));

    [HttpPost]
    public async Task<ActionResult<ExpenseDto>> Create(CreateExpenseRequest request, CancellationToken ct)
    {
        var expense = await expenses.CreateAsync(UserId, request, ct);
        return CreatedAtAction(nameof(Get), new { expenseId = expense.Id }, expense);
    }

    [HttpGet("{expenseId:guid}")]
    public async Task<ActionResult<ExpenseDto>> Get(Guid expenseId, CancellationToken ct)
        => Ok(await expenses.GetAsync(UserId, expenseId, ct));

    [HttpPatch("{expenseId:guid}")]
    public async Task<ActionResult<ExpenseDto>> Update(
        Guid expenseId, UpdateExpenseRequest request, CancellationToken ct)
        => Ok(await expenses.UpdateAsync(UserId, expenseId, request, ct));

    [HttpDelete("{expenseId:guid}")]
    public async Task<IActionResult> Delete(Guid expenseId, CancellationToken ct)
    {
        await expenses.DeleteAsync(UserId, expenseId, ct);
        return NoContent();
    }

    [HttpGet("{expenseId:guid}/history")]
    public async Task<ActionResult<IReadOnlyList<ExpenseRevisionDto>>> History(
        Guid expenseId, CancellationToken ct)
        => Ok(await expenses.GetHistoryAsync(UserId, expenseId, ct));

    [HttpGet("{expenseId:guid}/comments")]
    public async Task<ActionResult<IReadOnlyList<CommentDto>>> Comments(
        Guid expenseId, CancellationToken ct)
        => Ok(await expenses.GetCommentsAsync(UserId, expenseId, ct));

    [HttpPost("{expenseId:guid}/comments")]
    public async Task<ActionResult<CommentDto>> AddComment(
        Guid expenseId, CreateCommentRequest request, CancellationToken ct)
        => Ok(await expenses.AddCommentAsync(UserId, request with { ExpenseId = expenseId }, ct));

    [HttpDelete("comments/{commentId:guid}")]
    public async Task<IActionResult> DeleteComment(Guid commentId, CancellationToken ct)
    {
        await expenses.DeleteCommentAsync(UserId, commentId, ct);
        return NoContent();
    }

    /// <summary>Moves an expense to another group with its full history.</summary>
    [HttpPost("{expenseId:guid}/transfer")]
    public async Task<ActionResult<TransferExpenseResult>> Transfer(
        Guid expenseId, TransferExpenseRequest request, CancellationToken ct)
        => Ok(await lifecycle.TransferExpenseAsync(UserId, request with { ExpenseId = expenseId }, ct));

    // ---- recurring -------------------------------------------------------

    [HttpGet("recurring")]
    public async Task<ActionResult<IReadOnlyList<RecurringExpenseDto>>> ListRecurring(
        [FromQuery] Guid groupId, CancellationToken ct)
        => Ok(await recurring.ListAsync(UserId, groupId, ct));

    [HttpPost("recurring")]
    public async Task<ActionResult<RecurringExpenseDto>> CreateRecurring(
        CreateRecurringExpenseRequest request, CancellationToken ct)
        => Ok(await recurring.CreateAsync(UserId, request, ct));

    [HttpPost("recurring/{id:guid}/pause")]
    public async Task<ActionResult<RecurringExpenseDto>> PauseRecurring(
        Guid id, [FromQuery] bool paused = true, CancellationToken ct = default)
        => Ok(await recurring.SetPausedAsync(UserId, id, paused, ct));

    [HttpDelete("recurring/{id:guid}")]
    public async Task<IActionResult> DeleteRecurring(Guid id, CancellationToken ct)
    {
        await recurring.DeleteAsync(UserId, id, ct);
        return NoContent();
    }
}
