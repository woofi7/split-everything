using Microsoft.EntityFrameworkCore;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Common;
using SplitEverything.Application.Contracts.Settlements;
using SplitEverything.Application.Contracts.Sync;
using SplitEverything.Application.Services;
using SplitEverything.Domain.Algorithms;
using SplitEverything.Domain.Common;
using SplitEverything.Domain.Entities;
using SplitEverything.Infrastructure.Persistence;
using SplitEverything.Infrastructure.Sync;

namespace SplitEverything.Infrastructure.Services;

public sealed class SettlementService(
    AppDbContext db,
    ISyncWriter writer,
    IActivityService activity,
    ICurrencyConverter currency,
    ISyncBroadcaster broadcaster,
    IPushDispatcher push,
    IClock clock) : ISettlementService
{
    public async Task<SettlementDto> CreateAsync(
        Guid userId, CreateSettlementRequest request, CancellationToken ct = default)
    {
        var actor = await GroupAccess.RequireMemberAsync(db, userId, request.GroupId, ct);
        var group = await GroupAccess.RequireGroupAsync(db, request.GroupId, ct);
        GroupAccess.RequireWritable(group);

        if (request.ClientId is { } clientId)
        {
            var existing = await db.Settlements
                .FirstOrDefaultAsync(s => s.Id == clientId && s.GroupId == request.GroupId, ct);
            if (existing is not null) return await MapAsync(existing, ct);
        }

        if (request.Amount <= 0m)
            throw new ValidationException("A settlement amount must be greater than zero.");
        if (request.FromMemberId == request.ToMemberId)
            throw new ValidationException("A settlement needs two different members.");

        var settlementCurrency = GroupAccess.NormalizeCurrency(request.Currency);

        var members = await db.GroupMembers
            .Where(m => m.GroupId == request.GroupId && !m.IsDeleted)
            .Select(m => m.Id)
            .ToListAsync(ct);

        if (!members.Contains(request.FromMemberId) || !members.Contains(request.ToMemberId))
            throw new ValidationException("Both sides of a settlement must be members of this group.");

        var conversion = string.Equals(settlementCurrency, group.BaseCurrency, StringComparison.OrdinalIgnoreCase)
            ? new ConversionResult(request.Amount, 1m, clock.UtcNow)
            : await currency.ConvertAsync(request.Amount, settlementCurrency, group.BaseCurrency, request.SettledAt, ct);

        var settlement = new Settlement
        {
            Id = request.ClientId ?? Guid.CreateVersion7(),
            GroupId = request.GroupId,
            FromMemberId = request.FromMemberId,
            ToMemberId = request.ToMemberId,
            Amount = request.Amount,
            Currency = settlementCurrency,
            AmountInBaseCurrency = conversion.Amount,
            ExchangeRate = conversion.Rate,
            SettledAt = request.SettledAt,
            Note = request.Note?.Trim(),
            ReceiptId = request.ReceiptId,
            OriginLineageId = group.LineageId,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow
        };
        db.Settlements.Add(settlement);

        var deviceId = GroupService.DeviceFor(userId);
        var seq = await writer.RecordAsync(settlement, SyncEntityType.Settlement, request.GroupId,
            SyncOperation.Create, deviceId, userId, SettlementPayload(settlement), ct: ct);

        var names = await MemberNamesAsync(request.GroupId, ct);
        var summary = $"{names.GetValueOrDefault(request.FromMemberId, "Someone")} paid " +
                      $"{names.GetValueOrDefault(request.ToMemberId, "someone")} " +
                      $"{Format(request.Amount, settlementCurrency)}";

        await activity.RecordAsync(request.GroupId, ActivityKind.SettlementCreated, userId, actor.Id,
            SyncEntityType.Settlement, settlement.Id, summary,
            new { settlement.Amount, settlement.Currency }, ct);

        await db.SaveChangesAsync(ct);
        var settlementClock = settlement.Clock;
        db.ChangeTracker.Clear();

        await broadcaster.BroadcastAsync(request.GroupId, new SyncPushResult(
            [new SyncAcceptedDto(settlement.Id, settlement.Id, seq, settlementClock.Counters)],
            [], [], new Dictionary<Guid, long> { [request.GroupId] = seq }), deviceId, ct);

        await push.SendToGroupAsync(request.GroupId,
            new PushMessage(group.Name, summary, $"/groups/{request.GroupId}"), userId, ct);

        var saved = await db.Settlements.FirstAsync(s => s.Id == settlement.Id, ct);
        return await MapAsync(saved, ct);
    }

    public async Task<IReadOnlyList<SettlementDto>> ListAsync(
        Guid userId, Guid groupId, CancellationToken ct = default)
    {
        await GroupAccess.RequireMemberAsync(db, userId, groupId, ct);

        var names = await MemberNamesAsync(groupId, ct);

        return await db.Settlements
            .Where(s => s.GroupId == groupId && !s.IsDeleted)
            .OrderByDescending(s => s.SettledAt)
            .ThenByDescending(s => s.ServerSeq)
            .Select(s => new SettlementDto(
                s.Id, s.GroupId,
                s.FromMemberId, names.GetValueOrDefault(s.FromMemberId, "Unknown"),
                s.ToMemberId, names.GetValueOrDefault(s.ToMemberId, "Unknown"),
                s.Amount, s.Currency, s.AmountInBaseCurrency,
                s.SettledAt, s.Note, s.ReceiptId,
                new Dictionary<string, long>(), s.ServerSeq))
            .ToListAsync(ct);
    }

    public async Task DeleteAsync(Guid userId, Guid settlementId, CancellationToken ct = default)
    {
        var settlement = await db.Settlements.FirstOrDefaultAsync(s => s.Id == settlementId && !s.IsDeleted, ct)
                         ?? throw new NotFoundException($"Settlement {settlementId}");

        var actor = await GroupAccess.RequireMemberAsync(db, userId, settlement.GroupId, ct);
        var group = await GroupAccess.RequireGroupAsync(db, settlement.GroupId, ct);
        GroupAccess.RequireWritable(group);

        await writer.RecordAsync(settlement, SyncEntityType.Settlement, settlement.GroupId,
            SyncOperation.Delete, GroupService.DeviceFor(userId), userId,
            SettlementPayload(settlement), ct: ct);

        await activity.RecordAsync(settlement.GroupId, ActivityKind.SettlementDeleted, userId, actor.Id,
            SyncEntityType.Settlement, settlement.Id,
            $"{actor.DisplayName} removed a settlement", ct: ct);

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    public async Task<GroupBalanceDto> GetGroupBalanceAsync(
        Guid userId, Guid groupId, CancellationToken ct = default)
    {
        await GroupAccess.RequireMemberAsync(db, userId, groupId, ct);
        var group = await GroupAccess.RequireGroupAsync(db, groupId, ct);

        var members = await db.GroupMembers
            .Where(m => m.GroupId == groupId && !m.IsDeleted)
            .Select(m => new { m.Id, m.DisplayName })
            .ToListAsync(ct);
        var names = members.ToDictionary(m => m.Id, m => m.DisplayName);

        var (expenses, settlements) = await LoadLedgerAsync(groupId, ct);

        var balances = BalanceCalculator.NetBalances(
            members.Select(m => m.Id), expenses, settlements, group.BaseCurrency);

        var simplified = DebtSimplifier.Simplify(balances, group.BaseCurrency);
        var pairwise = BalanceCalculator.PairwiseDebts(expenses, settlements, group.BaseCurrency);

        return new GroupBalanceDto(
            groupId,
            group.BaseCurrency,
            balances
                .Select(b => new MemberBalanceDto(b.MemberId, names.GetValueOrDefault(b.MemberId, "Unknown"), b.Net))
                .OrderByDescending(b => b.Net)
                .ToList(),
            simplified
                .Select(t => new SuggestedTransferDto(
                    t.FromMemberId, names.GetValueOrDefault(t.FromMemberId, "Unknown"),
                    t.ToMemberId, names.GetValueOrDefault(t.ToMemberId, "Unknown"),
                    t.Amount, group.BaseCurrency))
                .ToList(),
            pairwise
                .Select(d => new SuggestedTransferDto(
                    d.FromMemberId, names.GetValueOrDefault(d.FromMemberId, "Unknown"),
                    d.ToMemberId, names.GetValueOrDefault(d.ToMemberId, "Unknown"),
                    d.Amount, group.BaseCurrency))
                .ToList());
    }

    public async Task<OverallBalanceDto> GetOverallBalanceAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new NotFoundException($"User {userId}");

        var memberships = await db.GroupMembers
            .Where(m => m.UserId == userId && m.Status == MembershipStatus.Active && !m.IsDeleted)
            .Select(m => new { m.Id, m.GroupId, m.Group!.Name, m.Group.BaseCurrency, m.Group.IsArchived })
            .ToListAsync(ct);

        var byGroup = new List<GroupNetDto>();
        var owedToMe = 0m;
        var iOwe = 0m;

        foreach (var membership in memberships.Where(m => !m.IsArchived))
        {
            var memberIds = await db.GroupMembers
                .Where(m => m.GroupId == membership.GroupId && !m.IsDeleted)
                .Select(m => m.Id)
                .ToListAsync(ct);

            var (expenses, settlements) = await LoadLedgerAsync(membership.GroupId, ct);

            var net = BalanceCalculator
                .NetBalances(memberIds, expenses, settlements, membership.BaseCurrency)
                .First(b => b.MemberId == membership.Id).Net;

            // Each group carries its own base currency, so the cross-group total has
            // to be converted rather than added up raw.
            var inUserCurrency = net == 0m || string.Equals(
                membership.BaseCurrency, user.DefaultCurrency, StringComparison.OrdinalIgnoreCase)
                ? net
                : (await currency.ConvertAsync(
                    Math.Abs(net), membership.BaseCurrency, user.DefaultCurrency, null, ct)).Amount
                  * (net < 0 ? -1m : 1m);

            byGroup.Add(new GroupNetDto(
                membership.GroupId, membership.Name, membership.BaseCurrency, net, inUserCurrency));

            if (inUserCurrency > 0) owedToMe += inUserCurrency;
            else iOwe += -inUserCurrency;
        }

        return new OverallBalanceDto(
            user.DefaultCurrency,
            CurrencyPrecision.Round(owedToMe, user.DefaultCurrency),
            CurrencyPrecision.Round(iOwe, user.DefaultCurrency),
            CurrencyPrecision.Round(owedToMe - iOwe, user.DefaultCurrency),
            byGroup.OrderByDescending(g => Math.Abs(g.NetInUserCurrency)).ToList());
    }

    public async Task NudgeAsync(Guid userId, NudgeRequest request, CancellationToken ct = default)
    {
        var actor = await GroupAccess.RequireMemberAsync(db, userId, request.GroupId, ct);
        var group = await GroupAccess.RequireGroupAsync(db, request.GroupId, ct);

        var target = await db.GroupMembers
                         .FirstOrDefaultAsync(m => m.Id == request.MemberId && m.GroupId == request.GroupId, ct)
                     ?? throw new NotFoundException($"Member {request.MemberId}");

        var balance = await GetGroupBalanceAsync(userId, request.GroupId, ct);
        var net = balance.Balances.FirstOrDefault(b => b.MemberId == request.MemberId)?.Net ?? 0m;

        if (net >= 0m)
            throw new ValidationException($"{target.DisplayName} does not owe anything in this group.");

        await activity.RecordAsync(request.GroupId, ActivityKind.DebtNudge, userId, actor.Id,
            SyncEntityType.GroupMember, target.Id,
            $"{actor.DisplayName} nudged {target.DisplayName} about {Format(-net, group.BaseCurrency)}",
            new { Amount = -net, group.BaseCurrency }, ct);

        await db.SaveChangesAsync(ct);

        // A placeholder member has nobody to notify yet; the reminder still lands in
        // the feed so it is not silently lost.
        if (target.UserId is { } targetUserId)
        {
            await push.SendToUsersAsync([targetUserId], new PushMessage(
                group.Name,
                string.IsNullOrWhiteSpace(request.Message)
                    ? $"{actor.DisplayName} reminded you: you owe {Format(-net, group.BaseCurrency)}"
                    : request.Message.Trim(),
                $"/groups/{request.GroupId}"), ct);
        }
    }

    private async Task<(List<BalanceExpense> Expenses, List<BalanceSettlement> Settlements)> LoadLedgerAsync(
        Guid groupId, CancellationToken ct)
    {
        var expenseRows = await db.Expenses
            .Where(e => e.GroupId == groupId && !e.IsDeleted)
            .Select(e => new
            {
                e.PaidByMemberId,
                e.AmountInBaseCurrency,
                Splits = e.Splits.Where(s => !s.IsDeleted)
                    .Select(s => new { s.MemberId, s.AmountInBaseCurrency }).ToList()
            })
            .ToListAsync(ct);

        var settlements = await db.Settlements
            .Where(s => s.GroupId == groupId && !s.IsDeleted)
            .Select(s => new BalanceSettlement(s.FromMemberId, s.ToMemberId, s.AmountInBaseCurrency))
            .ToListAsync(ct);

        var expenses = expenseRows.Select(e => new BalanceExpense(
            e.PaidByMemberId, e.AmountInBaseCurrency,
            e.Splits.Select(s => (s.MemberId, s.AmountInBaseCurrency)).ToList())).ToList();

        return (expenses, settlements);
    }

    private async Task<Dictionary<Guid, string>> MemberNamesAsync(Guid groupId, CancellationToken ct)
        => await db.GroupMembers
            .Where(m => m.GroupId == groupId)
            .ToDictionaryAsync(m => m.Id, m => m.DisplayName, ct);

    private async Task<SettlementDto> MapAsync(Settlement settlement, CancellationToken ct)
    {
        var names = await MemberNamesAsync(settlement.GroupId, ct);
        return new SettlementDto(
            settlement.Id, settlement.GroupId,
            settlement.FromMemberId, names.GetValueOrDefault(settlement.FromMemberId, "Unknown"),
            settlement.ToMemberId, names.GetValueOrDefault(settlement.ToMemberId, "Unknown"),
            settlement.Amount, settlement.Currency, settlement.AmountInBaseCurrency,
            settlement.SettledAt, settlement.Note, settlement.ReceiptId,
            settlement.Clock.Counters, settlement.ServerSeq);
    }

    internal static object SettlementPayload(Settlement settlement) => new
    {
        settlement.Id, settlement.GroupId, settlement.FromMemberId, settlement.ToMemberId,
        settlement.Amount, settlement.Currency, settlement.AmountInBaseCurrency,
        settlement.SettledAt, settlement.Note, settlement.IsDeleted
    };

    private static string Format(decimal amount, string currency)
        => $"{amount.ToString($"F{CurrencyPrecision.DecimalsFor(currency)}", System.Globalization.CultureInfo.InvariantCulture)} {currency}";
}
