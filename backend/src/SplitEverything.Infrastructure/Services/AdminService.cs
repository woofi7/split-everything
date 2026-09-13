using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Admin;
using SplitEverything.Application.Services;
using SplitEverything.Infrastructure.Auth;
using SplitEverything.Infrastructure.Persistence;

namespace SplitEverything.Infrastructure.Services;

/// <summary>
/// The server's own administration.
///
/// Everything else in this application is scoped by membership: you see a group
/// because you are in it, and no endpoint will tell you a group exists otherwise.
/// That is right, and it leaves the person whose machine this is unable to answer
/// the questions only they can be asked - what is taking up the disk, what is left
/// of the group that was split in two by mistake, which of these six abandoned
/// groups can go.
///
/// So this is deliberately narrow. It reads anything and it deletes a group that
/// has already been archived. It does not write into a group, and it does not put
/// the administrator into one: an account appearing in a household's balances
/// because somebody looked at it would be a worse bug than the one it solves.
/// </summary>
public sealed class AdminService(
    AppDbContext db,
    AdminOptions options,
    IReceiptStorage receipts,
    ILogger<AdminService> logger) : IAdminService
{
    public async Task<bool> IsAdminAsync(Guid userId, CancellationToken ct = default)
    {
        if (options.Emails.Length == 0) return false;

        var email = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(ct);

        return options.Includes(email);
    }

    public async Task<IReadOnlyList<AdminGroupDto>> GetGroupsAsync(
        Guid userId, CancellationToken ct = default)
    {
        await RequireAdminAsync(userId, ct);

        var rows = await db.Groups
            .AsNoTracking()
            .Select(group => new
            {
                Group = group,
                CreatedByName = db.Users
                    .Where(u => u.Id == group.CreatedByUserId)
                    .Select(u => u.DisplayName)
                    .FirstOrDefault(),
                MemberCount = group.Members.Count(m => m.Status == Domain.Common.MembershipStatus.Active),
                ExpenseCount = group.Expenses.Count(e => !e.IsDeleted),
                TotalSpend = group.Expenses
                    .Where(e => !e.IsDeleted)
                    .Sum(e => (decimal?)e.AmountInBaseCurrency) ?? 0m,
                LastActivityAt = db.ActivityLog
                    .Where(a => a.GroupId == group.Id)
                    .Max(a => (DateTimeOffset?)a.OccurredAt),
                IsMine = group.Members.Any(m => m.UserId == userId
                                                && m.Status != Domain.Common.MembershipStatus.Removed)
            })
            .ToListAsync(ct);

        return rows
            // In use first, then by whatever happened last: the ones worth acting
            // on are the quiet ones at the bottom.
            .OrderBy(row => row.Group.IsArchived)
            .ThenByDescending(row => row.LastActivityAt ?? row.Group.CreatedAt)
            .Select(row => Map(row.Group, row.CreatedByName, row.MemberCount, row.ExpenseCount,
                row.TotalSpend, row.LastActivityAt, row.IsMine))
            .ToList();
    }

    public async Task<AdminGroupDetailDto> GetGroupAsync(
        Guid userId, Guid groupId, CancellationToken ct = default)
    {
        await RequireAdminAsync(userId, ct);

        var group = await db.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == groupId, ct)
                    ?? throw new NotFoundException("That group");

        var members = await db.GroupMembers
            .AsNoTracking()
            .Where(m => m.GroupId == groupId)
            .OrderBy(m => m.Status)
            .ThenBy(m => m.DisplayName)
            .Select(m => new AdminMemberDto(
                m.Id,
                m.DisplayName,
                m.User == null ? null : m.User.Email,
                m.Role,
                m.Status,
                m.UserId == null))
            .ToListAsync(ct);

        var recent = await db.Expenses
            .AsNoTracking()
            .Where(e => e.GroupId == groupId && !e.IsDeleted)
            .OrderByDescending(e => e.SpentAt)
            .Take(20)
            .Select(e => new AdminExpenseDto(
                e.Id,
                e.Description,
                e.Amount,
                e.Currency,
                e.SpentAt,
                db.GroupMembers
                    .Where(m => m.Id == e.PaidByMemberId)
                    .Select(m => m.DisplayName)
                    .FirstOrDefault() ?? "Someone"))
            .ToListAsync(ct);

        var createdBy = await db.Users
            .Where(u => u.Id == group.CreatedByUserId)
            .Select(u => u.DisplayName)
            .FirstOrDefaultAsync(ct);

        var expenseCount = await db.Expenses.CountAsync(e => e.GroupId == groupId && !e.IsDeleted, ct);
        var totalSpend = await db.Expenses
            .Where(e => e.GroupId == groupId && !e.IsDeleted)
            .SumAsync(e => (decimal?)e.AmountInBaseCurrency, ct) ?? 0m;
        var lastActivityAt = await db.ActivityLog
            .Where(a => a.GroupId == groupId)
            .MaxAsync(a => (DateTimeOffset?)a.OccurredAt, ct);
        var isMine = await db.GroupMembers.AnyAsync(
            m => m.GroupId == groupId && m.UserId == userId
                 && m.Status != Domain.Common.MembershipStatus.Removed, ct);

        return new AdminGroupDetailDto(
            Map(group, createdBy,
                members.Count(m => m.Status == Domain.Common.MembershipStatus.Active),
                expenseCount, totalSpend, lastActivityAt, isMine),
            members,
            recent);
    }

    public async Task DeleteGroupAsync(Guid userId, Guid groupId, CancellationToken ct = default)
    {
        await RequireAdminAsync(userId, ct);

        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == groupId, ct)
                    ?? throw new NotFoundException("That group");

        if (!group.IsArchived)
        {
            // Archiving is the reversible step and the one everybody can take. This
            // is the other kind, so it only ever finishes something already stopped.
            throw new ValidationException("Archive this group before deleting it.");
        }

        var expenseIds = await db.Expenses
            .Where(e => e.GroupId == groupId)
            .Select(e => e.Id)
            .ToListAsync(ct);

        var itemIds = await db.ExpenseItems
            .Where(i => expenseIds.Contains(i.ExpenseId))
            .Select(i => i.Id)
            .ToListAsync(ct);

        // Receipt rows outlive the expense that points at them - the column is set
        // null rather than cascaded, and one photo can be shared - so the ones to
        // take with the group are those nothing else refers to.
        var orphanedReceipts = await db.Receipts
            .Where(r =>
                (db.Expenses.Any(e => e.GroupId == groupId && e.ReceiptId == r.Id)
                 || db.Settlements.Any(s => s.GroupId == groupId && s.ReceiptId == r.Id))
                && !db.Expenses.Any(e => e.GroupId != groupId && e.ReceiptId == r.Id)
                && !db.Settlements.Any(s => s.GroupId != groupId && s.ReceiptId == r.Id))
            .Select(r => new { r.Id, r.StorageKey })
            .ToListAsync(ct);

        var receiptIds = orphanedReceipts.Select(r => r.Id).ToList();

        // In dependency order, in one transaction. Postgres would cascade from the
        // group, but the constraints from a member to what they paid for restrict
        // rather than cascade: leaving the order to the database means the delete
        // sometimes works and sometimes stops half way with a foreign key error.
        //
        // Through the execution strategy, because the connection retries: a
        // transaction opened around a retriable operation has to be retriable
        // itself, and every statement in here is a delete, so running the block
        // twice ends where running it once does.
        var strategy = db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            await db.ExpenseItemShares.Where(s => itemIds.Contains(s.ExpenseItemId)).ExecuteDeleteAsync(ct);
            await db.ExpenseItems.Where(i => expenseIds.Contains(i.ExpenseId)).ExecuteDeleteAsync(ct);
            await db.ExpensePayers.Where(p => expenseIds.Contains(p.ExpenseId)).ExecuteDeleteAsync(ct);
            await db.ExpenseSplits.Where(s => expenseIds.Contains(s.ExpenseId)).ExecuteDeleteAsync(ct);
            await db.ExpenseComments.Where(c => expenseIds.Contains(c.ExpenseId)).ExecuteDeleteAsync(ct);
            await db.ExpenseRevisions.Where(r => expenseIds.Contains(r.ExpenseId)).ExecuteDeleteAsync(ct);
            await db.Expenses.Where(e => e.GroupId == groupId).ExecuteDeleteAsync(ct);

            await db.Settlements.Where(s => s.GroupId == groupId).ExecuteDeleteAsync(ct);
            await db.RecurringExpenses.Where(r => r.GroupId == groupId).ExecuteDeleteAsync(ct);
            await db.GroupInvites.Where(i => i.GroupId == groupId).ExecuteDeleteAsync(ct);
            await db.ActivityLog.Where(a => a.GroupId == groupId).ExecuteDeleteAsync(ct);
            await db.SyncLog.Where(l => l.GroupId == groupId).ExecuteDeleteAsync(ct);
            await db.SyncSnapshots.Where(s => s.GroupId == groupId).ExecuteDeleteAsync(ct);
            await db.SyncConflicts.Where(c => c.GroupId == groupId).ExecuteDeleteAsync(ct);
            await db.ImportBatches.Where(b => b.GroupId == groupId).ExecuteDeleteAsync(ct);
            await db.GroupLineageLinks
                .Where(l => l.SourceGroupId == groupId || l.TargetGroupId == groupId)
                .ExecuteDeleteAsync(ct);

            await db.GroupMembers.Where(m => m.GroupId == groupId).ExecuteDeleteAsync(ct);
            await db.Receipts.Where(r => receiptIds.Contains(r.Id)).ExecuteDeleteAsync(ct);
            await db.Groups.Where(g => g.Id == groupId).ExecuteDeleteAsync(ct);

            await transaction.CommitAsync(ct);
        });

        // Said out loud: this is the one action in the application that destroys
        // data, and a self-hosted install has no other record of it having happened.
        logger.LogWarning(
            "Administrator {UserId} deleted group {GroupId} ({GroupName}): {Expenses} expenses, {Receipts} receipts",
            userId, groupId, group.Name, expenseIds.Count, orphanedReceipts.Count);

        // After the commit, because the bytes are not part of the transaction and a
        // file that fails to go is a tidying problem, not a reason to keep the group.
        foreach (var receipt in orphanedReceipts)
        {
            try
            {
                await receipts.DeleteAsync(receipt.StorageKey, ct);
            }
            catch (Exception caught)
            {
                logger.LogWarning(caught, "Could not remove receipt file {StorageKey}", receipt.StorageKey);
            }
        }
    }

    private async Task RequireAdminAsync(Guid userId, CancellationToken ct)
    {
        if (!await IsAdminAsync(userId, ct))
        {
            throw new ForbiddenException("This is for whoever runs this server.");
        }
    }

    private static AdminGroupDto Map(
        Domain.Entities.Group group,
        string? createdByName,
        int memberCount,
        int expenseCount,
        decimal totalSpend,
        DateTimeOffset? lastActivityAt,
        bool isMine)
        => new(
            group.Id, group.Name, group.BaseCurrency, group.IconName, group.ColorHex,
            group.IsArchived, group.ArchivedAt, group.CreatedAt, lastActivityAt,
            createdByName ?? "Someone", memberCount, expenseCount, totalSpend, isMine);
}
