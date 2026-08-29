using Shouldly;
using SplitEverything.Domain.Algorithms;
using SplitEverything.Domain.Common;

namespace SplitEverything.Tests.Domain;

public class SplitCalculatorTests
{
    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Carol = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static List<SplitInput> Members(params Guid[] ids)
        => ids.Select(id => new SplitInput(id, null)).ToList();

    [Fact]
    public void Equal_split_divides_evenly_when_it_divides_cleanly()
    {
        var shares = SplitCalculator.Calculate(90m, "CAD", SplitType.Equal, Members(Alice, Bob, Carol));

        shares.ShouldAllBe(s => s.Amount == 30m);
    }

    [Fact]
    public void Equal_split_of_an_indivisible_amount_still_sums_to_the_total()
    {
        var shares = SplitCalculator.Calculate(10m, "CAD", SplitType.Equal, Members(Alice, Bob, Carol));

        shares.Sum(s => s.Amount).ShouldBe(10m);
        shares.Select(s => s.Amount).OrderBy(a => a).ShouldBe(new[] { 3.33m, 3.33m, 3.34m });
    }

    [Fact]
    public void Equal_split_is_deterministic_for_the_same_members()
    {
        var first = SplitCalculator.Calculate(10m, "CAD", SplitType.Equal, Members(Alice, Bob, Carol));
        var second = SplitCalculator.Calculate(10m, "CAD", SplitType.Equal, Members(Carol, Bob, Alice));

        // Offline devices computing the same split must agree to the cent.
        first.OrderBy(s => s.MemberId).Select(s => s.Amount)
            .ShouldBe(second.OrderBy(s => s.MemberId).Select(s => s.Amount));
    }

    [Fact]
    public void The_leftover_minor_unit_goes_to_the_lowest_member_id()
    {
        var shares = SplitCalculator.Calculate(10m, "CAD", SplitType.Equal, Members(Alice, Bob, Carol));

        // Pinned deliberately: the offline client runs the same algorithm, and both
        // sides must agree on who receives the extra cent or a synced expense would
        // be rewritten into different amounts than the person was shown.
        shares.Single(s => s.MemberId == Alice).Amount.ShouldBe(3.34m);
        shares.Single(s => s.MemberId == Bob).Amount.ShouldBe(3.33m);
        shares.Single(s => s.MemberId == Carol).Amount.ShouldBe(3.33m);
    }

    [Fact]
    public void Equal_split_across_one_member_gives_them_everything()
        => SplitCalculator.Calculate(42.42m, "CAD", SplitType.Equal, Members(Alice))
            .Single().Amount.ShouldBe(42.42m);

    [Fact]
    public void Zero_decimal_currencies_never_produce_fractions()
    {
        var shares = SplitCalculator.Calculate(10m, "JPY", SplitType.Equal, Members(Alice, Bob, Carol));

        shares.ShouldAllBe(s => s.Amount == Math.Truncate(s.Amount));
        shares.Sum(s => s.Amount).ShouldBe(10m);
    }

    [Fact]
    public void Three_decimal_currencies_split_to_the_thousandth()
    {
        var shares = SplitCalculator.Calculate(1m, "KWD", SplitType.Equal, Members(Alice, Bob, Carol));

        shares.Sum(s => s.Amount).ShouldBe(1m);
        shares.Select(s => s.Amount).OrderBy(a => a).ShouldBe(new[] { 0.333m, 0.333m, 0.334m });
    }

    [Fact]
    public void Percentage_split_applies_each_percentage_and_keeps_the_input()
    {
        var shares = SplitCalculator.Calculate(200m, "CAD", SplitType.Percentage, [
            new SplitInput(Alice, 25m),
            new SplitInput(Bob, 75m)
        ]);

        shares.Single(s => s.MemberId == Alice).Amount.ShouldBe(50m);
        shares.Single(s => s.MemberId == Bob).Amount.ShouldBe(150m);
        shares.Single(s => s.MemberId == Alice).InputValue.ShouldBe(25m);
    }

    [Fact]
    public void Percentage_split_with_repeating_decimals_still_sums_to_the_total()
    {
        var shares = SplitCalculator.Calculate(100m, "CAD", SplitType.Percentage, [
            new SplitInput(Alice, 33.33m),
            new SplitInput(Bob, 33.33m),
            new SplitInput(Carol, 33.34m)
        ]);

        shares.Sum(s => s.Amount).ShouldBe(100m);
    }

    [Fact]
    public void Percentage_split_rejects_percentages_that_do_not_reach_one_hundred()
        => Should.Throw<ArgumentException>(() => SplitCalculator.Calculate(
            100m, "CAD", SplitType.Percentage,
            [new SplitInput(Alice, 40m), new SplitInput(Bob, 40m)]));

    [Fact]
    public void Shares_split_weights_by_share_count()
    {
        var shares = SplitCalculator.Calculate(120m, "CAD", SplitType.Shares, [
            new SplitInput(Alice, 1m),
            new SplitInput(Bob, 2m),
            new SplitInput(Carol, 3m)
        ]);

        shares.Single(s => s.MemberId == Alice).Amount.ShouldBe(20m);
        shares.Single(s => s.MemberId == Bob).Amount.ShouldBe(40m);
        shares.Single(s => s.MemberId == Carol).Amount.ShouldBe(60m);
        shares.Single(s => s.MemberId == Carol).InputValue.ShouldBe(3m);
    }

    [Fact]
    public void Shares_split_handles_a_member_with_no_share()
    {
        var shares = SplitCalculator.Calculate(100m, "CAD", SplitType.Shares, [
            new SplitInput(Alice, 0m),
            new SplitInput(Bob, 1m)
        ]);

        shares.Single(s => s.MemberId == Alice).Amount.ShouldBe(0m);
        shares.Single(s => s.MemberId == Bob).Amount.ShouldBe(100m);
    }

    [Fact]
    public void Shares_split_rejects_an_all_zero_weighting()
        => Should.Throw<ArgumentException>(() => SplitCalculator.Calculate(
            100m, "CAD", SplitType.Shares,
            [new SplitInput(Alice, 0m), new SplitInput(Bob, 0m)]));

    [Fact]
    public void Exact_split_accepts_amounts_that_add_up()
    {
        var shares = SplitCalculator.Calculate(100m, "CAD", SplitType.ExactAmount, [
            new SplitInput(Alice, 30.50m),
            new SplitInput(Bob, 69.50m)
        ]);

        shares.Single(s => s.MemberId == Alice).Amount.ShouldBe(30.50m);
        shares.Sum(s => s.Amount).ShouldBe(100m);
    }

    [Fact]
    public void Exact_split_rejects_amounts_that_miss_the_total()
        => Should.Throw<ArgumentException>(() => SplitCalculator.Calculate(
            100m, "CAD", SplitType.ExactAmount,
            [new SplitInput(Alice, 30m), new SplitInput(Bob, 60m)]));

    [Fact]
    public void A_negative_total_produces_negative_shares_summing_to_the_total()
    {
        var shares = SplitCalculator.Calculate(-10m, "CAD", SplitType.Equal, Members(Alice, Bob, Carol));

        shares.Sum(s => s.Amount).ShouldBe(-10m);
        shares.ShouldAllBe(s => s.Amount < 0);
    }

    [Fact]
    public void An_empty_participant_list_is_rejected()
        => Should.Throw<ArgumentException>(() => SplitCalculator.Calculate(
            10m, "CAD", SplitType.Equal, []));

    [Fact]
    public void A_member_listed_twice_is_rejected()
        => Should.Throw<ArgumentException>(() => SplitCalculator.Calculate(
            10m, "CAD", SplitType.Equal, Members(Alice, Alice)));

    [Fact]
    public void Itemized_must_go_through_the_itemized_entry_point()
        => Should.Throw<InvalidOperationException>(() => SplitCalculator.Calculate(
            10m, "CAD", SplitType.Itemized, Members(Alice)));

    [Fact]
    public void An_unknown_split_type_is_rejected()
        => Should.Throw<ArgumentOutOfRangeException>(() => SplitCalculator.Calculate(
            10m, "CAD", (SplitType)99, Members(Alice)));

    [Fact]
    public void Itemized_charges_each_line_to_whoever_had_it()
    {
        var shares = SplitCalculator.CalculateItemized(30m, "CAD", [
            new ItemizedLine(20m, 1, [Alice]),
            new ItemizedLine(10m, 1, [Bob])
        ], [Alice, Bob]);

        shares.Single(s => s.MemberId == Alice).Amount.ShouldBe(20m);
        shares.Single(s => s.MemberId == Bob).Amount.ShouldBe(10m);
    }

    [Fact]
    public void Itemized_splits_a_shared_line_between_its_participants()
    {
        var shares = SplitCalculator.CalculateItemized(30m, "CAD", [
            new ItemizedLine(30m, 1, [Alice, Bob])
        ], [Alice, Bob]);

        shares.Single(s => s.MemberId == Alice).Amount.ShouldBe(15m);
        shares.Single(s => s.MemberId == Bob).Amount.ShouldBe(15m);
    }

    [Fact]
    public void Itemized_multiplies_a_line_by_its_quantity()
    {
        var shares = SplitCalculator.CalculateItemized(30m, "CAD", [
            new ItemizedLine(10m, 3, [Alice])
        ], [Alice, Bob]);

        shares.Single(s => s.MemberId == Alice).Amount.ShouldBe(30m);
    }

    [Fact]
    public void Itemized_spreads_tax_and_tip_in_proportion_to_what_each_person_ordered()
    {
        // 30 of items, 36 charged: the 6 of tax and tip follows the order sizes 2:1.
        var shares = SplitCalculator.CalculateItemized(36m, "CAD", [
            new ItemizedLine(20m, 1, [Alice]),
            new ItemizedLine(10m, 1, [Bob])
        ], [Alice, Bob]);

        shares.Single(s => s.MemberId == Alice).Amount.ShouldBe(24m);
        shares.Single(s => s.MemberId == Bob).Amount.ShouldBe(12m);
        shares.Sum(s => s.Amount).ShouldBe(36m);
    }

    [Fact]
    public void Itemized_handles_a_discount_that_makes_the_total_lower_than_the_items()
    {
        var shares = SplitCalculator.CalculateItemized(27m, "CAD", [
            new ItemizedLine(20m, 1, [Alice]),
            new ItemizedLine(10m, 1, [Bob])
        ], [Alice, Bob]);

        shares.Sum(s => s.Amount).ShouldBe(27m);
    }

    [Fact]
    public void Itemized_falls_back_to_the_group_for_a_line_with_nobody_on_it()
    {
        var shares = SplitCalculator.CalculateItemized(10m, "CAD", [
            new ItemizedLine(10m, 1, [])
        ], [Alice, Bob]);

        shares.Single(s => s.MemberId == Alice).Amount.ShouldBe(5m);
        shares.Single(s => s.MemberId == Bob).Amount.ShouldBe(5m);
    }

    [Fact]
    public void Itemized_with_no_items_falls_back_to_an_equal_split()
    {
        var shares = SplitCalculator.CalculateItemized(10m, "CAD", [], [Alice, Bob]);

        shares.Sum(s => s.Amount).ShouldBe(10m);
        shares.Count.ShouldBe(2);
    }

    [Fact]
    public void Itemized_with_neither_items_nor_participants_is_rejected()
        => Should.Throw<ArgumentException>(() => SplitCalculator.CalculateItemized(10m, "CAD", [], []));

    [Fact]
    public void Itemized_rejects_a_line_with_no_participants_and_no_fallback()
        => Should.Throw<ArgumentException>(() => SplitCalculator.CalculateItemized(
            10m, "CAD", [new ItemizedLine(10m, 1, [])], []));

    [Fact]
    public void Itemized_sums_to_the_total_across_a_long_awkward_receipt()
    {
        var lines = Enumerable.Range(0, 17)
            .Select(i => new ItemizedLine(3.33m, 1, i % 2 == 0 ? [Alice, Bob] : [Carol]))
            .ToList();
        var total = 71.11m;

        var shares = SplitCalculator.CalculateItemized(total, "CAD", lines, [Alice, Bob, Carol]);

        shares.Sum(s => s.Amount).ShouldBe(total);
    }

    [Theory]
    [InlineData(0.01, 3)]
    [InlineData(0.02, 3)]
    [InlineData(100.01, 7)]
    [InlineData(9999.99, 11)]
    [InlineData(1, 100)]
    public void Every_equal_split_sums_exactly_to_the_total(double total, int memberCount)
    {
        var members = Enumerable.Range(0, memberCount)
            .Select(_ => new SplitInput(Guid.NewGuid(), null))
            .ToList();

        var shares = SplitCalculator.Calculate((decimal)total, "CAD", SplitType.Equal, members);

        shares.Sum(s => s.Amount).ShouldBe((decimal)total);
    }
}
