using Shouldly;
using SplitEverything.Domain.Algorithms;

namespace SplitEverything.Tests.Domain;

public class BalanceCalculatorTests
{
    private static readonly Guid A = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");
    private static readonly Guid B = Guid.Parse("bbbbbbbb-1111-1111-1111-111111111111");
    private static readonly Guid C = Guid.Parse("cccccccc-1111-1111-1111-111111111111");

    private static BalanceExpense Expense(Guid payer, decimal amount, params (Guid Member, decimal Share)[] splits)
        => new(payer, amount, splits.Select(s => (s.Member, s.Share)).ToList());

    [Fact]
    public void A_group_with_no_activity_is_all_zeros()
    {
        var balances = BalanceCalculator.NetBalances([A, B], [], []);

        balances.ShouldAllBe(b => b.Net == 0m);
        balances.Count.ShouldBe(2);
    }

    [Fact]
    public void The_payer_is_credited_their_outlay_less_their_own_share()
    {
        var balances = BalanceCalculator.NetBalances(
            [A, B],
            [Expense(A, 100m, (A, 50m), (B, 50m))],
            []);

        balances.Single(b => b.MemberId == A).Net.ShouldBe(50m);
        balances.Single(b => b.MemberId == B).Net.ShouldBe(-50m);
    }

    [Fact]
    public void Balances_always_sum_to_zero()
    {
        var balances = BalanceCalculator.NetBalances(
            [A, B, C],
            [
                Expense(A, 90m, (A, 30m), (B, 30m), (C, 30m)),
                Expense(B, 45m, (A, 15m), (B, 15m), (C, 15m))
            ],
            []);

        balances.Sum(b => b.Net).ShouldBe(0m);
    }

    [Fact]
    public void A_settlement_moves_the_balance_back_toward_zero()
    {
        var balances = BalanceCalculator.NetBalances(
            [A, B],
            [Expense(A, 100m, (A, 50m), (B, 50m))],
            [new BalanceSettlement(B, A, 50m)]);

        balances.ShouldAllBe(b => b.Net == 0m);
    }

    [Fact]
    public void Overpaying_a_settlement_flips_the_debt()
    {
        var balances = BalanceCalculator.NetBalances(
            [A, B],
            [Expense(A, 100m, (A, 50m), (B, 50m))],
            [new BalanceSettlement(B, A, 70m)]);

        balances.Single(b => b.MemberId == B).Net.ShouldBe(20m);
        balances.Single(b => b.MemberId == A).Net.ShouldBe(-20m);
    }

    [Fact]
    public void A_member_who_left_still_carries_their_history()
    {
        // C is no longer on the roster but paid for something before leaving.
        var balances = BalanceCalculator.NetBalances(
            [A, B],
            [Expense(C, 60m, (A, 30m), (B, 30m))],
            []);

        balances.Single(b => b.MemberId == C).Net.ShouldBe(60m);
        balances.Sum(b => b.Net).ShouldBe(0m);
    }

    [Fact]
    public void Pairwise_debts_show_who_owes_whom_before_simplification()
    {
        var debts = BalanceCalculator.PairwiseDebts(
            [Expense(A, 100m, (A, 50m), (B, 50m))],
            []);

        var debt = debts.ShouldHaveSingleItem();
        debt.FromMemberId.ShouldBe(B);
        debt.ToMemberId.ShouldBe(A);
        debt.Amount.ShouldBe(50m);
    }

    [Fact]
    public void Opposite_pairwise_debts_net_off_into_one_direction()
    {
        var debts = BalanceCalculator.PairwiseDebts(
            [
                Expense(A, 100m, (B, 100m)),
                Expense(B, 40m, (A, 40m))
            ],
            []);

        var debt = debts.ShouldHaveSingleItem();
        debt.FromMemberId.ShouldBe(B);
        debt.ToMemberId.ShouldBe(A);
        debt.Amount.ShouldBe(60m);
    }

    [Fact]
    public void A_fully_repaid_pair_disappears_from_the_pairwise_view()
        => BalanceCalculator.PairwiseDebts(
            [Expense(A, 50m, (B, 50m))],
            [new BalanceSettlement(B, A, 50m)]).ShouldBeEmpty();

    [Fact]
    public void The_payers_own_share_creates_no_self_debt()
        => BalanceCalculator.PairwiseDebts(
            [Expense(A, 50m, (A, 50m))],
            []).ShouldBeEmpty();

    [Fact]
    public void Pairwise_and_net_views_agree_on_each_members_total()
    {
        var expenses = new[]
        {
            Expense(A, 90m, (A, 30m), (B, 30m), (C, 30m)),
            Expense(B, 60m, (A, 20m), (B, 20m), (C, 20m)),
            Expense(C, 30m, (A, 10m), (B, 10m), (C, 10m))
        };
        var settlements = new[] { new BalanceSettlement(C, A, 15m) };

        var net = BalanceCalculator.NetBalances([A, B, C], expenses, settlements)
            .ToDictionary(b => b.MemberId, b => b.Net);

        var fromPairwise = new Dictionary<Guid, decimal> { [A] = 0m, [B] = 0m, [C] = 0m };
        foreach (var debt in BalanceCalculator.PairwiseDebts(expenses, settlements))
        {
            fromPairwise[debt.FromMemberId] -= debt.Amount;
            fromPairwise[debt.ToMemberId] += debt.Amount;
        }

        foreach (var (member, expected) in net)
            fromPairwise[member].ShouldBe(expected);
    }

    [Fact]
    public void A_simplified_plan_built_from_the_net_view_settles_the_group()
    {
        var expenses = new[]
        {
            Expense(A, 120m, (A, 40m), (B, 40m), (C, 40m)),
            Expense(B, 33.33m, (A, 11.11m), (B, 11.11m), (C, 11.11m))
        };

        var balances = BalanceCalculator.NetBalances([A, B, C], expenses, []);
        var net = balances.ToDictionary(b => b.MemberId, b => b.Net);

        foreach (var transfer in DebtSimplifier.Simplify(balances))
        {
            net[transfer.FromMemberId] += transfer.Amount;
            net[transfer.ToMemberId] -= transfer.Amount;
        }

        net.Values.ShouldAllBe(v => Math.Abs(v) < 0.01m);
    }
}
