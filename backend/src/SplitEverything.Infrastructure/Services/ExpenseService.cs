using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Expenses;
using SplitEverything.Application.Contracts.Sync;
using SplitEverything.Application.Services;
using SplitEverything.Domain.Algorithms;
using SplitEverything.Domain.Common;
using SplitEverything.Domain.Entities;
using SplitEverything.Domain.Sync;
using SplitEverything.Infrastructure.Persistence;
using SplitEverything.Infrastructure.Sync;

namespace SplitEverything.Infrastructure.Services;

public sealed class ExpenseService(
    AppDbContext db,
    ISyncWriter writer,
    IActivityService activity,
    ICurrencyConverter currency,
    ISyncBroadcaster broadcaster,
    IPushDispatcher push,
    IClock clock) : IExpenseService
{
    public async Task<ExpenseDto> CreateAsync(Guid userId, CreateExpenseRequest request, CancellationToken ct = default)
    {
        var actor = await GroupAccess.RequireMemberAsync(db, userId, request.GroupId, ct);
        var group = await GroupAccess.RequireGroupAsync(db, request.GroupId, ct);
        GroupAccess.RequireWritable(group);

        // An offline client retries its queued creates; the client id makes the
        // replay a no-op instead of a second charge.
        if (request.ClientId is { } clientId)
        {
            var existing = await db.Expenses
                .FirstOrDefaultAsync(e => e.Id == clientId && e.GroupId == request.GroupId, ct);
            if (existing is not null) return await GetAsync(userId, existing.Id, ct);
        }

        var description = GroupAccess.RequireText(request.Description, "Description", 500);
        var expenseCurrency = GroupAccess.NormalizeCurrency(request.Currency);
        if (request.Amount <= 0m)
            throw new ValidationException("An expense amount must be greater than zero.");

        var members = await LoadMemberIdsAsync(request.GroupId, ct);
        ValidateParticipants(request.PaidByMemberId, request.Splits, request.Items, members);

        var shares = ComputeShares(request.Amount, expenseCurrency, request.SplitType, request.Splits, request.Items);
        var conversion = await ConvertAsync(request.Amount, expenseCurrency, group.BaseCurrency, request.SpentAt, ct);

        var expense = new Expense
        {
            Id = request.ClientId ?? Guid.CreateVersion7(),
            GroupId = request.GroupId,
            PaidByMemberId = request.PaidByMemberId,
            Description = description,
            Amount = request.Amount,
            Currency = expenseCurrency,
            AmountInBaseCurrency = conversion.Amount,
            ExchangeRate = conversion.Rate,
            ExchangeRateAsOf = conversion.RateAsOf,
            SpentAt = request.SpentAt,
            SplitType = request.SplitType,
            ReceiptId = request.ReceiptId,
            Notes = request.Notes?.Trim(),
            OriginLineageId = group.LineageId,
            ImportFingerprint = request.ImportFingerprint,
            ImportBatchId = request.ImportBatchId,
            Revision = 1,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow
        };
        db.Expenses.Add(expense);

        AddSplits(expense, shares, conversion.Rate, group.BaseCurrency);
        AddItems(expense, request.Items);

        var deviceId = GroupService.DeviceFor(userId);
        var seq = await writer.RecordAsync(expense, SyncEntityType.Expense, request.GroupId,
            SyncOperation.Create, deviceId, userId, ExpensePayload(expense), ct: ct);

        RecordRevision(expense, userId, deviceId, "Created");

        await activity.RecordAsync(request.GroupId, ActivityKind.ExpenseCreated, userId, actor.Id,
            SyncEntityType.Expense, expense.Id,
            $"{actor.DisplayName} added {description} ({FormatAmount(expense.Amount, expenseCurrency)})",
            new { expense.Amount, expense.Currency, expense.Description }, ct);

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        await BroadcastAsync(request.GroupId, expense.Id, SyncEntityType.Expense, seq, expense.Clock, deviceId, ct);
        await push.SendToGroupAsync(request.GroupId, new PushMessage(
            group.Name,
            $"{actor.DisplayName} added {description} ({FormatAmount(expense.Amount, expenseCurrency)})",
            $"/groups/{request.GroupId}/expenses/{expense.Id}",
            $"expense-{expense.Id}"), userId, ct);

        return await GetAsync(userId, expense.Id, ct);
    }

    public async Task<ExpenseDto> GetAsync(Guid userId, Guid expenseId, CancellationToken ct = default)
    {
        var expense = await db.Expenses
                          .Include(e => e.Splits.Where(s => !s.IsDeleted))
                          .Include(e => e.Items.Where(i => !i.IsDeleted)).ThenInclude(i => i.Shares)
                          .FirstOrDefaultAsync(e => e.Id == expenseId && !e.IsDeleted, ct)
                      ?? throw new NotFoundException($"Expense {expenseId}");

        await GroupAccess.RequireMemberAsync(db, userId, expense.GroupId, ct);
        return await MapAsync(expense, ct);
    }

    public async Task<Paged<ExpenseDto>> ListAsync(Guid userId, ExpenseQuery query, CancellationToken ct = default)
    {
        IQueryable<Expense> expenses = db.Expenses
            .Include(e => e.Splits.Where(s => !s.IsDeleted))
            .Include(e => e.Items.Where(i => !i.IsDeleted)).ThenInclude(i => i.Shares)
            .Where(e => !e.IsDeleted);

        if (query.GroupId is { } groupId)
        {
            await GroupAccess.RequireMemberAsync(db, userId, groupId, ct);
            expenses = expenses.Where(e => e.GroupId == groupId);
        }
        else
        {
            var myGroupIds = db.GroupMembers
                .Where(m => m.UserId == userId && m.Status == MembershipStatus.Active && !m.IsDeleted)
                .Select(m => m.GroupId);
            expenses = expenses.Where(e => myGroupIds.Contains(e.GroupId));
        }

        if (query.MemberId is { } memberId)
            expenses = expenses.Where(e =>
                e.PaidByMemberId == memberId || e.Splits.Any(s => s.MemberId == memberId && !s.IsDeleted));
        if (query.From is { } from)
            expenses = expenses.Where(e => e.SpentAt >= from);
        if (query.To is { } to)
            expenses = expenses.Where(e => e.SpentAt <= to);
        if (!string.IsNullOrWhiteSpace(query.Search))
            expenses = expenses.Where(e => EF.Functions.ILike(e.Description, $"%{query.Search.Trim()}%"));

        var page = new PageRequest(query.Page, query.PageSize);
        var total = await expenses.CountAsync(ct);

        var rows = await expenses
            .OrderByDescending(e => e.SpentAt)
            .ThenByDescending(e => e.ServerSeq)
            .Skip(page.Skip)
            .Take(page.Clamped)
            .ToListAsync(ct);

        var items = new List<ExpenseDto>(rows.Count);
        foreach (var row in rows) items.Add(await MapAsync(row, ct));

        return new Paged<ExpenseDto>(items, total, page.Page, page.Clamped);
    }

    public async Task<ExpenseDto> UpdateAsync(
        Guid userId, Guid expenseId, UpdateExpenseRequest request, CancellationToken ct = default)
    {
        var expense = await db.Expenses
                          .Include(e => e.Splits)
                          .Include(e => e.Items).ThenInclude(i => i.Shares)
                          .FirstOrDefaultAsync(e => e.Id == expenseId && !e.IsDeleted, ct)
                      ?? throw new NotFoundException($"Expense {expenseId}");

        var actor = await GroupAccess.RequireMemberAsync(db, userId, expense.GroupId, ct);
        var group = await GroupAccess.RequireGroupAsync(db, expense.GroupId, ct);
        GroupAccess.RequireWritable(group);

        // The client tells us which revision it edited. If ours has moved on
        // concurrently, we refuse rather than silently dropping the other edit.
        if (request.BaseVectorClock is not null)
        {
            var incoming = VectorClock.From(request.BaseVectorClock);
            if (SyncArbiter.Decide(expense.Clock, incoming) == SyncDecision.Conflict
                || incoming.CompareWith(expense.Clock) == ClockOrdering.Before)
            {
                throw new SyncConflictException(
                    "This expense changed on another device. Reload before editing.");
            }
        }

        var previousDescription = expense.Description;
        var changes = new List<string>();

        if (request.Description is not null)
        {
            var next = GroupAccess.RequireText(request.Description, "Description", 500);
            if (next != expense.Description) changes.Add("description");
            expense.Description = next;
        }

        if (request.Amount is { } amount)
        {
            if (amount <= 0m) throw new ValidationException("An expense amount must be greater than zero.");
            if (amount != expense.Amount) changes.Add("amount");
            expense.Amount = amount;
        }

        if (request.Currency is not null)
        {
            var next = GroupAccess.NormalizeCurrency(request.Currency);
            if (next != expense.Currency) changes.Add("currency");
            expense.Currency = next;
        }

        if (request.SpentAt is { } spentAt)
        {
            if (spentAt != expense.SpentAt) changes.Add("date");
            expense.SpentAt = spentAt;
        }

        if (request.ReceiptId is not null) expense.ReceiptId = request.ReceiptId;
        if (request.Notes is not null) expense.Notes = request.Notes.Trim();
        if (request.PaidByMemberId is { } payer)
        {
            if (payer != expense.PaidByMemberId) changes.Add("payer");
            expense.PaidByMemberId = payer;
        }

        var splitType = request.SplitType ?? expense.SplitType;
        var members = await LoadMemberIdsAsync(expense.GroupId, ct);

        var splitInputs = request.Splits
                          ?? expense.Splits.Where(s => !s.IsDeleted)
                              .Select(s => new SplitInputDto(s.MemberId, s.InputValue))
                              .ToList();

        var itemInputs = request.Items
                         ?? (expense.Items.Count == 0
                             ? null
                             : expense.Items.Where(i => !i.IsDeleted)
                                 .OrderBy(i => i.SortOrder)
                                 .Select(i => new ExpenseItemDto(i.Id, i.Description, i.Amount, i.Quantity,
                                     i.SortOrder, i.Shares.Select(s => s.MemberId).ToList()))
                                 .ToList());

        ValidateParticipants(expense.PaidByMemberId, splitInputs, itemInputs, members);

        var shares = ComputeShares(expense.Amount, expense.Currency, splitType, splitInputs, itemInputs);
        var conversion = await ConvertAsync(expense.Amount, expense.Currency, group.BaseCurrency, expense.SpentAt, ct);

        expense.SplitType = splitType;
        expense.AmountInBaseCurrency = conversion.Amount;
        expense.ExchangeRate = conversion.Rate;
        expense.ExchangeRateAsOf = conversion.RateAsOf;
        expense.Revision += 1;

        ReplaceSplits(expense, shares, conversion.Rate, group.BaseCurrency);
        if (request.Items is not null) ReplaceItems(expense, request.Items);

        var deviceId = GroupService.DeviceFor(userId);
        var seq = await writer.RecordAsync(expense, SyncEntityType.Expense, expense.GroupId,
            SyncOperation.Update, deviceId, userId, ExpensePayload(expense), ct: ct);

        RecordRevision(expense, userId, deviceId,
            changes.Count == 0 ? "Edited" : $"Changed {string.Join(", ", changes)}");

        await activity.RecordAsync(expense.GroupId, ActivityKind.ExpenseUpdated, userId, actor.Id,
            SyncEntityType.Expense, expense.Id,
            $"{actor.DisplayName} edited {previousDescription}",
            new { expense.Revision, Changes = changes }, ct);

        await db.SaveChangesAsync(ct);
        var updatedClock = expense.Clock;
        db.ChangeTracker.Clear();

        await BroadcastAsync(expense.GroupId, expense.Id, SyncEntityType.Expense, seq, updatedClock, deviceId, ct);

        return await GetAsync(userId, expenseId, ct);
    }

    public async Task DeleteAsync(Guid userId, Guid expenseId, CancellationToken ct = default)
    {
        var expense = await db.Expenses
                          .Include(e => e.Splits)
                          .FirstOrDefaultAsync(e => e.Id == expenseId && !e.IsDeleted, ct)
                      ?? throw new NotFoundException($"Expense {expenseId}");

        var actor = await GroupAccess.RequireMemberAsync(db, userId, expense.GroupId, ct);
        var group = await GroupAccess.RequireGroupAsync(db, expense.GroupId, ct);
        GroupAccess.RequireWritable(group);

        var deviceId = GroupService.DeviceFor(userId);
        var seq = await writer.RecordAsync(expense, SyncEntityType.Expense, expense.GroupId,
            SyncOperation.Delete, deviceId, userId, ExpensePayload(expense), ct: ct);

        // Tombstone the splits too, so a peer replaying the delete does not keep a
        // dangling share that would skew its local balance.
        foreach (var split in expense.Splits.Where(s => !s.IsDeleted))
        {
            split.IsDeleted = true;
            split.DeletedAt = clock.UtcNow;
        }

        await activity.RecordAsync(expense.GroupId, ActivityKind.ExpenseDeleted, userId, actor.Id,
            SyncEntityType.Expense, expense.Id,
            $"{actor.DisplayName} deleted {expense.Description}", ct: ct);

        await db.SaveChangesAsync(ct);
        var deletedClock = expense.Clock;
        var groupId = expense.GroupId;
        db.ChangeTracker.Clear();

        await BroadcastAsync(groupId, expenseId, SyncEntityType.Expense, seq, deletedClock, deviceId, ct);
    }

    public async Task<IReadOnlyList<ExpenseRevisionDto>> GetHistoryAsync(
        Guid userId, Guid expenseId, CancellationToken ct = default)
    {
        var expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == expenseId, ct)
                      ?? throw new NotFoundException($"Expense {expenseId}");
        await GroupAccess.RequireMemberAsync(db, userId, expense.GroupId, ct);

        return await db.ExpenseRevisions
            .Where(r => r.ExpenseId == expenseId)
            .OrderBy(r => r.Revision)
            .Select(r => new ExpenseRevisionDto(
                r.Id, r.Revision, r.EditedByUserId,
                db.Users.Where(u => u.Id == r.EditedByUserId).Select(u => u.DisplayName).FirstOrDefault(),
                r.EditedAt, r.ChangeSummary, r.GroupId, r.SnapshotJson))
            .ToListAsync(ct);
    }

    public async Task<CommentDto> AddCommentAsync(
        Guid userId, CreateCommentRequest request, CancellationToken ct = default)
    {
        var expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == request.ExpenseId && !e.IsDeleted, ct)
                      ?? throw new NotFoundException($"Expense {request.ExpenseId}");

        var actor = await GroupAccess.RequireMemberAsync(db, userId, expense.GroupId, ct);
        var group = await GroupAccess.RequireGroupAsync(db, expense.GroupId, ct);
        GroupAccess.RequireWritable(group);

        if (request.ClientId is { } clientId)
        {
            var existing = await db.ExpenseComments.FirstOrDefaultAsync(c => c.Id == clientId, ct);
            if (existing is not null) return await MapCommentAsync(existing, ct);
        }

        var body = GroupAccess.RequireText(request.Body, "Comment", 4000);

        if (request.ParentCommentId is { } parentId)
        {
            var parent = await db.ExpenseComments
                             .FirstOrDefaultAsync(c => c.Id == parentId && c.ExpenseId == request.ExpenseId, ct)
                         ?? throw new NotFoundException($"Comment {parentId}");
            // One level of threading only: a reply to a reply attaches to the top.
            if (parent.ParentCommentId is not null)
                request = request with { ParentCommentId = parent.ParentCommentId };
        }

        var comment = new ExpenseComment
        {
            Id = request.ClientId ?? Guid.CreateVersion7(),
            ExpenseId = request.ExpenseId,
            GroupId = expense.GroupId,
            AuthorMemberId = actor.Id,
            ParentCommentId = request.ParentCommentId,
            Body = body,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow
        };
        db.ExpenseComments.Add(comment);

        var deviceId = GroupService.DeviceFor(userId);
        var seq = await writer.RecordAsync(comment, SyncEntityType.ExpenseComment, expense.GroupId,
            SyncOperation.Create, deviceId, userId,
            new { comment.Id, comment.ExpenseId, comment.AuthorMemberId, comment.ParentCommentId, comment.Body }, ct: ct);

        await activity.RecordAsync(expense.GroupId, ActivityKind.CommentPosted, userId, actor.Id,
            SyncEntityType.ExpenseComment, comment.Id,
            $"{actor.DisplayName} commented on {expense.Description}", ct: ct);

        await db.SaveChangesAsync(ct);
        var commentClock = comment.Clock;
        db.ChangeTracker.Clear();

        await BroadcastAsync(expense.GroupId, comment.Id, SyncEntityType.ExpenseComment, seq, commentClock, deviceId, ct);
        await push.SendToGroupAsync(expense.GroupId, new PushMessage(
            group.Name, $"{actor.DisplayName}: {Truncate(body, 80)}",
            $"/groups/{expense.GroupId}/expenses/{expense.Id}"), userId, ct);

        var saved = await db.ExpenseComments.FirstAsync(c => c.Id == comment.Id, ct);
        return await MapCommentAsync(saved, ct);
    }

    public async Task<IReadOnlyList<CommentDto>> GetCommentsAsync(
        Guid userId, Guid expenseId, CancellationToken ct = default)
    {
        var expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == expenseId, ct)
                      ?? throw new NotFoundException($"Expense {expenseId}");
        await GroupAccess.RequireMemberAsync(db, userId, expense.GroupId, ct);

        var rows = await db.ExpenseComments
            .Where(c => c.ExpenseId == expenseId && !c.IsDeleted)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new
            {
                Comment = c,
                AuthorName = c.AuthorMember!.DisplayName,
                AuthorAvatar = c.AuthorMember.User == null ? null : c.AuthorMember.User.AvatarUrl
            })
            .ToListAsync(ct);

        var byParent = rows
            .Where(r => r.Comment.ParentCommentId is not null)
            .GroupBy(r => r.Comment.ParentCommentId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        return rows
            .Where(r => r.Comment.ParentCommentId is null)
            .Select(r => new CommentDto(
                r.Comment.Id, r.Comment.ExpenseId, r.Comment.AuthorMemberId, r.AuthorName, r.AuthorAvatar,
                null, r.Comment.Body, r.Comment.CreatedAt, r.Comment.EditedAt,
                byParent.GetValueOrDefault(r.Comment.Id, [])
                    .Select(reply => new CommentDto(
                        reply.Comment.Id, reply.Comment.ExpenseId, reply.Comment.AuthorMemberId,
                        reply.AuthorName, reply.AuthorAvatar, reply.Comment.ParentCommentId,
                        reply.Comment.Body, reply.Comment.CreatedAt, reply.Comment.EditedAt, []))
                    .ToList()))
            .ToList();
    }

    public async Task DeleteCommentAsync(Guid userId, Guid commentId, CancellationToken ct = default)
    {
        var comment = await db.ExpenseComments.FirstOrDefaultAsync(c => c.Id == commentId && !c.IsDeleted, ct)
                      ?? throw new NotFoundException($"Comment {commentId}");

        var actor = await GroupAccess.RequireMemberAsync(db, userId, comment.GroupId, ct);
        if (comment.AuthorMemberId != actor.Id && actor.Role is not (GroupRole.Owner or GroupRole.Admin))
            throw new ForbiddenException("Only the author can delete this comment.");

        await writer.RecordAsync(comment, SyncEntityType.ExpenseComment, comment.GroupId,
            SyncOperation.Delete, GroupService.DeviceFor(userId), userId, new { comment.Id }, ct: ct);

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    // ---- internals -------------------------------------------------------

    private async Task<HashSet<Guid>> LoadMemberIdsAsync(Guid groupId, CancellationToken ct)
        => (await db.GroupMembers
            .Where(m => m.GroupId == groupId && !m.IsDeleted)
            .Select(m => m.Id)
            .ToListAsync(ct)).ToHashSet();

    private static void ValidateParticipants(
        Guid payerId,
        IReadOnlyList<SplitInputDto>? splits,
        IReadOnlyList<ExpenseItemDto>? items,
        HashSet<Guid> members)
    {
        if (!members.Contains(payerId))
            throw new ValidationException("The payer must be a member of this group.");

        if (splits is null || splits.Count == 0)
            throw new ValidationException("An expense needs at least one participant.");

        foreach (var split in splits)
        {
            if (!members.Contains(split.MemberId))
                throw new ValidationException("Every participant must be a member of this group.");
        }

        if (splits.Select(s => s.MemberId).Distinct().Count() != splits.Count)
            throw new ValidationException("A participant cannot appear twice in a split.");

        foreach (var memberId in items?.SelectMany(i => i.MemberIds) ?? [])
        {
            if (!members.Contains(memberId))
                throw new ValidationException("Every item participant must be a member of this group.");
        }
    }

    private static IReadOnlyList<SplitShare> ComputeShares(
        decimal amount, string expenseCurrency, SplitType splitType,
        IReadOnlyList<SplitInputDto> splits, IReadOnlyList<ExpenseItemDto>? items)
    {
        try
        {
            if (splitType == SplitType.Itemized)
            {
                if (items is null || items.Count == 0)
                    throw new ValidationException("An itemized expense needs at least one item.");

                return SplitCalculator.CalculateItemized(
                    amount, expenseCurrency,
                    items.Select(i => new ItemizedLine(i.Amount, i.Quantity, i.MemberIds)).ToList(),
                    splits.Select(s => s.MemberId).ToList());
            }

            return SplitCalculator.Calculate(amount, expenseCurrency, splitType,
                splits.Select(s => new SplitInput(s.MemberId, s.Value)).ToList());
        }
        catch (ArgumentException ex)
        {
            // The calculator guards the arithmetic; surfacing it as a validation
            // failure keeps a bad split a 400 rather than a 500.
            throw new ValidationException(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            throw new ValidationException(ex.Message);
        }
    }

    private async Task<ConversionResult> ConvertAsync(
        decimal amount, string from, string to, DateTimeOffset asOf, CancellationToken ct)
        => string.Equals(from, to, StringComparison.OrdinalIgnoreCase)
            ? new ConversionResult(amount, 1m, clock.UtcNow)
            : await currency.ConvertAsync(amount, from, to, asOf, ct);

    private void AddSplits(Expense expense, IReadOnlyList<SplitShare> shares, decimal rate, string baseCurrency)
    {
        foreach (var share in shares)
        {
            db.ExpenseSplits.Add(new ExpenseSplit
            {
                ExpenseId = expense.Id,
                GroupId = expense.GroupId,
                MemberId = share.MemberId,
                Amount = share.Amount,
                AmountInBaseCurrency = CurrencyPrecision.Round(share.Amount * rate, baseCurrency),
                InputValue = share.InputValue,
                Clock = expense.Clock,
                CreatedAt = clock.UtcNow,
                UpdatedAt = clock.UtcNow
            });
        }

        RebalanceBaseAmounts(expense, shares, baseCurrency);
    }

    private void ReplaceSplits(Expense expense, IReadOnlyList<SplitShare> shares, decimal rate, string baseCurrency)
    {
        var wanted = shares.ToDictionary(s => s.MemberId);

        foreach (var split in expense.Splits.ToList())
        {
            if (wanted.TryGetValue(split.MemberId, out var share))
            {
                split.Amount = share.Amount;
                split.AmountInBaseCurrency = CurrencyPrecision.Round(share.Amount * rate, baseCurrency);
                split.InputValue = share.InputValue;
                split.IsDeleted = false;
                split.DeletedAt = null;
                split.UpdatedAt = clock.UtcNow;
                wanted.Remove(split.MemberId);
            }
            else
            {
                split.IsDeleted = true;
                split.DeletedAt = clock.UtcNow;
                split.Amount = 0m;
                split.AmountInBaseCurrency = 0m;
            }
        }

        foreach (var share in wanted.Values)
        {
            expense.Splits.Add(new ExpenseSplit
            {
                ExpenseId = expense.Id,
                GroupId = expense.GroupId,
                MemberId = share.MemberId,
                Amount = share.Amount,
                AmountInBaseCurrency = CurrencyPrecision.Round(share.Amount * rate, baseCurrency),
                InputValue = share.InputValue,
                Clock = expense.Clock,
                CreatedAt = clock.UtcNow,
                UpdatedAt = clock.UtcNow
            });
        }

        RebalanceBaseAmounts(expense, shares, baseCurrency);
    }

    /// <summary>
    /// Converting each share separately can leave the base-currency shares off the
    /// converted total by a cent. Balances are computed from the base amounts, so
    /// that cent would surface later as a debt nobody owes.
    /// </summary>
    private static void RebalanceBaseAmounts(Expense expense, IReadOnlyList<SplitShare> shares, string baseCurrency)
    {
        var live = expense.Splits.Where(s => !s.IsDeleted).ToList();
        if (live.Count == 0) return;

        var residue = CurrencyPrecision.Round(
            expense.AmountInBaseCurrency - live.Sum(s => s.AmountInBaseCurrency), baseCurrency);
        if (residue == 0m) return;

        var target = live
            .OrderByDescending(s => Math.Abs(s.AmountInBaseCurrency))
            .ThenBy(s => s.MemberId)
            .First();

        target.AmountInBaseCurrency = CurrencyPrecision.Round(
            target.AmountInBaseCurrency + residue, baseCurrency);
    }

    private void AddItems(Expense expense, IReadOnlyList<ExpenseItemDto>? items)
    {
        foreach (var item in items ?? [])
        {
            var entity = new ExpenseItem
            {
                Id = item.Id ?? Guid.CreateVersion7(),
                ExpenseId = expense.Id,
                GroupId = expense.GroupId,
                Description = GroupAccess.RequireText(item.Description, "Item description", 300),
                Amount = item.Amount,
                Quantity = Math.Max(1, item.Quantity),
                SortOrder = item.SortOrder,
                Clock = expense.Clock,
                CreatedAt = clock.UtcNow,
                UpdatedAt = clock.UtcNow
            };
            db.ExpenseItems.Add(entity);

            foreach (var memberId in item.MemberIds.Distinct())
            {
                db.ExpenseItemShares.Add(new ExpenseItemShare
                {
                    ExpenseItemId = entity.Id,
                    MemberId = memberId
                });
            }
        }
    }

    private void ReplaceItems(Expense expense, IReadOnlyList<ExpenseItemDto> items)
    {
        db.ExpenseItemShares.RemoveRange(expense.Items.SelectMany(i => i.Shares));
        db.ExpenseItems.RemoveRange(expense.Items);
        expense.Items.Clear();
        AddItems(expense, items);
    }

    private void RecordRevision(Expense expense, Guid userId, string deviceId, string summary)
        => db.ExpenseRevisions.Add(new ExpenseRevision
        {
            ExpenseId = expense.Id,
            GroupId = expense.GroupId,
            Revision = expense.Revision,
            EditedByUserId = userId,
            EditedByDeviceId = deviceId,
            EditedAt = clock.UtcNow,
            VectorClockJson = expense.VectorClockJson,
            SnapshotJson = JsonSerializer.Serialize(ExpensePayload(expense)),
            ChangeSummary = summary
        });

    private async Task BroadcastAsync(
        Guid groupId, Guid entityId, SyncEntityType type, long seq,
        VectorClock entityClock, string deviceId, CancellationToken ct)
    {
        var accepted = new SyncAcceptedDto(entityId, entityId, seq, entityClock.Counters);
        await broadcaster.BroadcastAsync(groupId,
            new SyncPushResult([accepted], [], [], new Dictionary<Guid, long> { [groupId] = seq }),
            deviceId, ct);
    }

    private async Task<ExpenseDto> MapAsync(Expense expense, CancellationToken ct)
    {
        var memberNames = await db.GroupMembers
            .Where(m => m.GroupId == expense.GroupId)
            .ToDictionaryAsync(m => m.Id, m => m.DisplayName, ct);

        var commentCount = await db.ExpenseComments
            .CountAsync(c => c.ExpenseId == expense.Id && !c.IsDeleted, ct);

        return new ExpenseDto(
            expense.Id, expense.GroupId, expense.PaidByMemberId,
            memberNames.GetValueOrDefault(expense.PaidByMemberId, "Unknown"),
            expense.Description, expense.Amount, expense.Currency,
            expense.AmountInBaseCurrency, expense.ExchangeRate, expense.SpentAt,
            expense.SplitType, expense.ReceiptId, expense.Notes,
            expense.Revision, expense.RecurringExpenseId, expense.OriginGroupId,
            expense.Splits.Where(s => !s.IsDeleted)
                .Select(s => new ExpenseSplitDto(
                    s.MemberId, memberNames.GetValueOrDefault(s.MemberId, "Unknown"),
                    s.Amount, s.AmountInBaseCurrency, s.InputValue))
                .OrderBy(s => s.MemberName)
                .ToList(),
            expense.Items.Where(i => !i.IsDeleted)
                .OrderBy(i => i.SortOrder)
                .Select(i => new ExpenseItemDto(i.Id, i.Description, i.Amount, i.Quantity,
                    i.SortOrder, i.Shares.Select(s => s.MemberId).ToList()))
                .ToList(),
            commentCount,
            expense.Clock.Counters,
            expense.ServerSeq,
            expense.CreatedAt, expense.UpdatedAt);
    }

    private async Task<CommentDto> MapCommentAsync(ExpenseComment comment, CancellationToken ct)
    {
        var author = await db.GroupMembers
            .Where(m => m.Id == comment.AuthorMemberId)
            .Select(m => new { m.DisplayName, Avatar = m.User == null ? null : m.User.AvatarUrl })
            .FirstOrDefaultAsync(ct);

        return new CommentDto(comment.Id, comment.ExpenseId, comment.AuthorMemberId,
            author?.DisplayName ?? "Unknown", author?.Avatar, comment.ParentCommentId,
            comment.Body, comment.CreatedAt, comment.EditedAt, []);
    }

    internal static object ExpensePayload(Expense expense) => new
    {
        expense.Id, expense.GroupId, expense.PaidByMemberId, expense.Description,
        expense.Amount, expense.Currency, expense.AmountInBaseCurrency, expense.ExchangeRate,
        expense.SpentAt, SplitType = (int)expense.SplitType,
        expense.ReceiptId, expense.Notes, expense.Revision, expense.IsDeleted,
        expense.OriginGroupId, expense.OriginLineageId,
        Splits = expense.Splits.Where(s => !s.IsDeleted)
            .Select(s => new { s.MemberId, s.Amount, s.AmountInBaseCurrency, s.InputValue })
            .ToList(),
        Items = expense.Items.Where(i => !i.IsDeleted)
            .Select(i => new
            {
                i.Id, i.Description, i.Amount, i.Quantity, i.SortOrder,
                Members = i.Shares.Select(s => s.MemberId).ToList()
            })
            .ToList()
    };

    private static string FormatAmount(decimal amount, string currency)
        => $"{amount.ToString($"F{CurrencyPrecision.DecimalsFor(currency)}", System.Globalization.CultureInfo.InvariantCulture)} {currency}";

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max] + "...";
}
