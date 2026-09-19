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
                Payers = e.Payers.Where(y => !y.IsDeleted)
                    .Select(y => new { y.MemberId, y.AmountInBaseCurrency }).ToList(),
                Splits = e.Splits.Where(s => !s.IsDeleted)
                    .Select(s => new { s.MemberId, s.AmountInBaseCurrency }).ToList()
            })
            .ToListAsync(ct);

        var settlements = await db.Settlements
            .Where(s => s.GroupId == groupId && !s.IsDeleted)
            .Select(s => new BalanceSettlement(s.FromMemberId, s.ToMemberId, s.AmountInBaseCurrency))
            .ToListAsync(ct);

        var expenses = expenseRows.Select(e => new BalanceExpense(
            e.Payers.Select(p => (p.MemberId, p.AmountInBaseCurrency)).ToList(),
            e.Splits.Select(s => (s.MemberId, s.AmountInBaseCurrency)).ToList())).ToList();

        return (expenses, settlements);
    }

    public async Task<CrossGroupBalanceDto> GetCrossGroupBalanceAsync(
        Guid userId, Guid withUserId, CancellationToken ct = default)
    {
        if (userId == withUserId)
            throw new ValidationException("Pick somebody else to settle with.");

        var other = await db.Users.FirstOrDefaultAsync(u => u.Id == withUserId, ct)
                    ?? throw new NotFoundException($"User {withUserId}");

        var shared = await SharedGroupsAsync(userId, withUserId, ct);
        var offsets = PlanOffsets(shared);

        return new CrossGroupBalanceDto(
            withUserId,
            other.DisplayName,
            shared.Select(g => new CrossGroupGroupDto(g.GroupId, g.Name, g.Currency, g.Net, g.CanSettle)).ToList(),
            offsets,
            Remainders(shared, offsets));
    }

    public async Task<OffsetAcrossGroupsResult> OffsetAcrossGroupsAsync(
        Guid userId, OffsetAcrossGroupsRequest request, CancellationToken ct = default)
    {
        if (userId == request.WithUserId)
            throw new ValidationException("Pick somebody else to settle with.");

        var shared = await SharedGroupsAsync(userId, request.WithUserId, ct);
        var offsets = PlanOffsets(shared);

        if (offsets.Count == 0)
            throw new ValidationException(
                "There is nothing to cancel out: what you owe each other does not face in both directions.");

        var byGroup = shared.ToDictionary(g => g.GroupId);
        var now = clock.UtcNow;
        var deviceId = GroupService.DeviceFor(userId);
        var touched = new Dictionary<Guid, long>();
        var recorded = new List<Settlement>();

        foreach (var offset in offsets)
        {
            var owed = byGroup[offset.OwedGroupId];
            var owing = byGroup[offset.OwingGroupId];

            var theyPay = NewOffsetSettlement(owed, owed.TheirMemberId, owed.MyMemberId, offset.Amount,
                now, request.Note, owing.Name);
            var iPay = NewOffsetSettlement(owing, owing.MyMemberId, owing.TheirMemberId, offset.Amount,
                now, request.Note, owed.Name);

            theyPay.OffsetSettlementId = iPay.Id;
            iPay.OffsetSettlementId = theyPay.Id;

            db.Settlements.Add(theyPay);
            db.Settlements.Add(iPay);
            recorded.Add(theyPay);
            recorded.Add(iPay);

            foreach (var (settlement, group, counterpart) in new[]
                     {
                         (theyPay, owed, owing.Name),
                         (iPay, owing, owed.Name)
                     })
            {
                var seq = await writer.RecordAsync(settlement, SyncEntityType.Settlement, group.GroupId,
                    SyncOperation.Create, deviceId, userId, SettlementPayload(settlement), ct: ct);
                touched[group.GroupId] = seq;

                await activity.RecordAsync(group.GroupId, ActivityKind.SettlementCreated, userId, group.MyMemberId,
                    SyncEntityType.Settlement, settlement.Id,
                    $"{Format(offset.Amount, group.Currency)} cancelled against {counterpart}",
                    new { settlement.Amount, settlement.Currency }, ct);
            }
        }

        await db.SaveChangesAsync(ct);
        var clocks = recorded.ToDictionary(s => s.Id, s => s.Clock.Counters);
        db.ChangeTracker.Clear();

        foreach (var (groupId, seq) in touched)
        {
            var accepted = recorded
                .Where(s => s.GroupId == groupId)
                .Select(s => new SyncAcceptedDto(s.Id, s.Id, s.ServerSeq, clocks[s.Id]))
                .ToList();

            await broadcaster.BroadcastAsync(groupId, new SyncPushResult(
                accepted, [], [], new Dictionary<Guid, long> { [groupId] = seq }), deviceId, ct);
        }

        var after = await SharedGroupsAsync(userId, request.WithUserId, ct);

        return new OffsetAcrossGroupsResult(offsets, Remainders(after, []), recorded.Count);
    }

    private sealed record SharedGroup(
        Guid GroupId, string Name, string Currency, Guid LineageId, Guid MyMemberId, Guid TheirMemberId,
        decimal Net, bool CanSettle);

    private async Task<List<SharedGroup>> SharedGroupsAsync(Guid userId, Guid withUserId, CancellationToken ct)
    {
        var mine = await db.GroupMembers
            .Where(m => m.UserId == userId && m.Status == MembershipStatus.Active && !m.IsDeleted)
            .Select(m => new
            {
                m.Id, m.GroupId, m.Group!.Name, m.Group.BaseCurrency, m.Group.LineageId, m.Group.IsArchived
            })
            .ToListAsync(ct);

        var theirs = await db.GroupMembers
            .Where(m => m.UserId == withUserId && m.Status == MembershipStatus.Active && !m.IsDeleted)
            .Select(m => new { m.Id, m.GroupId })
            .ToListAsync(ct);

        var theirMember = theirs.ToDictionary(m => m.GroupId, m => m.Id);
        var shared = new List<SharedGroup>();

        foreach (var membership in mine)
        {
            if (!theirMember.TryGetValue(membership.GroupId, out var theirId)) continue;

            var (expenses, settlements) = await LoadLedgerAsync(membership.GroupId, ct);
            var pairwise = BalanceCalculator.PairwiseDebts(expenses, settlements, membership.BaseCurrency);

            var net = 0m;
            foreach (var debt in pairwise)
            {
                if (debt.FromMemberId == theirId && debt.ToMemberId == membership.Id) net += debt.Amount;
                else if (debt.FromMemberId == membership.Id && debt.ToMemberId == theirId) net -= debt.Amount;
            }

            if (net == 0m) continue;

            shared.Add(new SharedGroup(
                membership.GroupId, membership.Name, membership.BaseCurrency, membership.LineageId,
                membership.Id, theirId, net, !membership.IsArchived));
        }

        return shared.OrderByDescending(g => Math.Abs(g.Net)).ToList();
    }

    private static List<PlannedOffsetDto> PlanOffsets(IEnumerable<SharedGroup> shared)
    {
        var offsets = new List<PlannedOffsetDto>();

        foreach (var byCurrency in shared.Where(g => g.CanSettle).GroupBy(g => g.Currency))
        {
            var owed = byCurrency.Where(g => g.Net > 0m)
                .Select(g => (g.GroupId, g.Name, Left: g.Net)).OrderByDescending(g => g.Left).ToList();
            var owing = byCurrency.Where(g => g.Net < 0m)
                .Select(g => (g.GroupId, g.Name, Left: -g.Net)).OrderByDescending(g => g.Left).ToList();

            var i = 0;
            var j = 0;

            while (i < owed.Count && j < owing.Count)
            {
                var amount = Math.Min(owed[i].Left, owing[j].Left);
                if (amount > 0m)
                {
                    offsets.Add(new PlannedOffsetDto(
                        owed[i].GroupId, owed[i].Name, owing[j].GroupId, owing[j].Name,
                        amount, byCurrency.Key));
                }

                owed[i] = owed[i] with { Left = owed[i].Left - amount };
                owing[j] = owing[j] with { Left = owing[j].Left - amount };

                if (owed[i].Left <= 0m) i++;
                if (owing[j].Left <= 0m) j++;
            }
        }

        return offsets;
    }

    private static List<CrossGroupRemainderDto> Remainders(
        IEnumerable<SharedGroup> shared, IReadOnlyList<PlannedOffsetDto> offsets)
    {
        var remaining = new List<CrossGroupRemainderDto>();

        foreach (var byCurrency in shared.GroupBy(g => g.Currency))
        {
            var left = byCurrency.ToDictionary(g => g.GroupId, g => g.Net);

            foreach (var offset in offsets.Where(o => o.Currency == byCurrency.Key))
            {
                left[offset.OwedGroupId] -= offset.Amount;
                left[offset.OwingGroupId] += offset.Amount;
            }

            var net = CurrencyPrecision.Round(left.Values.Sum(), byCurrency.Key);
            var carrying = left.Where(entry => entry.Value != 0m).Select(entry => entry.Key).ToList();
            var where = carrying.Count == 1
                ? byCurrency.First(g => g.GroupId == carrying[0])
                : null;

            remaining.Add(new CrossGroupRemainderDto(byCurrency.Key, net, where?.GroupId, where?.Name));
        }

        return remaining;
    }

    private Settlement NewOffsetSettlement(
        SharedGroup group, Guid fromMemberId, Guid toMemberId, decimal amount,
        DateTimeOffset when, string? note, string counterpartName)
        => new()
        {
            Id = Guid.CreateVersion7(),
            GroupId = group.GroupId,
            FromMemberId = fromMemberId,
            ToMemberId = toMemberId,
            Amount = amount,
            Currency = group.Currency,
            AmountInBaseCurrency = amount,
            ExchangeRate = 1m,
            SettledAt = when,
            Note = string.IsNullOrWhiteSpace(note) ? $"Cancelled against {counterpartName}" : note.Trim(),
            OriginLineageId = group.LineageId,
            CreatedAt = when,
            UpdatedAt = when
        };

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
        settlement.SettledAt, settlement.Note, settlement.OffsetSettlementId, settlement.IsDeleted
    };

    private static string Format(decimal amount, string currency)
        => $"{amount.ToString($"F{CurrencyPrecision.DecimalsFor(currency)}", System.Globalization.CultureInfo.InvariantCulture)} {currency}";
}
