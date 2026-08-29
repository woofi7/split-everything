using Shouldly;
using SplitEverything.Domain.Algorithms;

namespace SplitEverything.Tests.Domain;

public class DebtSimplifierTests
{
    private static readonly Guid A = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid C = Guid.Parse("cccccccc-0000-0000-0000-000000000003");
    private static readonly Guid D = Guid.Parse("dddddddd-0000-0000-0000-000000000004");

    [Fact]
    public void An_already_settled_group_needs_no_transfers()
        => DebtSimplifier.Simplify([
            new MemberBalance(A, 0m),
            new MemberBalance(B, 0m)
        ]).ShouldBeEmpty();

    [Fact]
    public void A_single_debt_becomes_a_single_transfer()
    {
        var transfers = DebtSimplifier.Simplify([
            new MemberBalance(A, -25m),
            new MemberBalance(B, 25m)
        ]);

        var transfer = transfers.ShouldHaveSingleItem();
        transfer.FromMemberId.ShouldBe(A);
        transfer.ToMemberId.ShouldBe(B);
        transfer.Amount.ShouldBe(25m);
    }

    [Fact]
    public void A_debt_chain_collapses_into_one_transfer()
    {
        // A owes B, B owes C the same amount: B drops out entirely.
        var transfers = DebtSimplifier.Simplify([
            new MemberBalance(A, -30m),
            new MemberBalance(B, 0m),
            new MemberBalance(C, 30m)
        ]);

        var transfer = transfers.ShouldHaveSingleItem();
        transfer.FromMemberId.ShouldBe(A);
        transfer.ToMemberId.ShouldBe(C);
    }

    [Fact]
    public void Four_people_settle_in_at_most_three_transfers()
    {
        var transfers = DebtSimplifier.Simplify([
            new MemberBalance(A, -40m),
            new MemberBalance(B, -20m),
            new MemberBalance(C, 35m),
            new MemberBalance(D, 25m)
        ]);

        transfers.Count.ShouldBeLessThanOrEqualTo(3);
        transfers.Sum(t => t.Amount).ShouldBe(60m);
    }

    [Fact]
    public void The_plan_leaves_everyone_at_zero()
    {
        var balances = new List<MemberBalance>
        {
            new(A, -73.21m), new(B, 12.50m), new(C, 45.71m), new(D, 15m)
        };

        var net = balances.ToDictionary(b => b.MemberId, b => b.Net);
        foreach (var transfer in DebtSimplifier.Simplify(balances))
        {
            net[transfer.FromMemberId] += transfer.Amount;
            net[transfer.ToMemberId] -= transfer.Amount;
        }

        net.Values.ShouldAllBe(v => Math.Abs(v) < 0.01m);
    }

    [Fact]
    public void Nobody_both_pays_and_receives_in_the_same_plan()
    {
        var transfers = DebtSimplifier.Simplify([
            new MemberBalance(A, -50m),
            new MemberBalance(B, -30m),
            new MemberBalance(C, 80m)
        ]);

        var payers = transfers.Select(t => t.FromMemberId).ToHashSet();
        var receivers = transfers.Select(t => t.ToMemberId).ToHashSet();

        payers.Intersect(receivers).ShouldBeEmpty();
    }

    [Fact]
    public void Transfer_count_never_exceeds_participants_minus_one()
    {
        var random = new Random(20260831);
        for (var run = 0; run < 200; run++)
        {
            var count = random.Next(2, 12);
            var ids = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList();
            var amounts = ids.Select(_ => Math.Round((decimal)(random.NextDouble() * 400 - 200), 2)).ToList();

            // Force the balances to sum to zero, as a real group's always do.
            amounts[^1] -= amounts.Sum();

            var balances = ids.Zip(amounts, (id, amount) => new MemberBalance(id, amount)).ToList();
            var transfers = DebtSimplifier.Simplify(balances);

            transfers.Count.ShouldBeLessThanOrEqualTo(count - 1);

            var net = balances.ToDictionary(b => b.MemberId, b => b.Net);
            foreach (var transfer in transfers)
            {
                net[transfer.FromMemberId] += transfer.Amount;
                net[transfer.ToMemberId] -= transfer.Amount;
            }
            net.Values.ShouldAllBe(v => Math.Abs(v) < 0.02m);
        }
    }

    [Fact]
    public void Every_transfer_moves_a_positive_amount()
    {
        var transfers = DebtSimplifier.Simplify([
            new MemberBalance(A, -0.02m),
            new MemberBalance(B, -100m),
            new MemberBalance(C, 100.02m)
        ]);

        transfers.ShouldAllBe(t => t.Amount > 0m);
    }

    [Fact]
    public void Sub_cent_noise_is_ignored_rather_than_creating_a_transfer()
        => DebtSimplifier.Simplify([
            new MemberBalance(A, -0.004m),
            new MemberBalance(B, 0.004m)
        ]).ShouldBeEmpty();

    [Fact]
    public void All_creditors_and_no_debtors_yields_nothing()
        => DebtSimplifier.Simplify([
            new MemberBalance(A, 10m),
            new MemberBalance(B, 20m)
        ]).ShouldBeEmpty();

    [Fact]
    public void The_plan_is_deterministic_across_input_orderings()
    {
        List<MemberBalance> balances = [new(A, -40m), new(B, -20m), new(C, 35m), new(D, 25m)];

        var first = DebtSimplifier.Simplify(balances);
        var second = DebtSimplifier.Simplify([.. balances.AsEnumerable().Reverse()]);

        first.ShouldBe(second);
    }

    [Fact]
    public void Ties_are_broken_by_member_id_so_two_devices_agree()
    {
        // B and C are owed exactly the same; the plan must not depend on list order.
        List<MemberBalance> balances = [new(A, -20m), new(B, 10m), new(C, 10m)];

        DebtSimplifier.Simplify(balances)
            .ShouldBe(DebtSimplifier.Simplify([balances[2], balances[1], balances[0]]));
    }

    [Fact]
    public void Zero_decimal_currencies_settle_in_whole_units()
    {
        var transfers = DebtSimplifier.Simplify([
            new MemberBalance(A, -1000m),
            new MemberBalance(B, 1000m)
        ], "JPY");

        transfers.ShouldHaveSingleItem().Amount.ShouldBe(1000m);
    }

    [Fact]
    public void An_empty_group_yields_nothing()
        => DebtSimplifier.Simplify([]).ShouldBeEmpty();
}
