using Shouldly;
using SplitEverything.Domain.Algorithms;

namespace SplitEverything.Tests.Domain;

public class ExpenseFingerprintTests
{
    private static readonly DateTimeOffset Day = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_known_transaction_hashes_to_a_pinned_value()
    {
        // Pinned as a wire contract. The browser computes this same fingerprint to
        // ask "do I already have this transaction?", because the statement itself
        // never leaves the device. The identical constant is asserted in
        // frontend/tests/domain/fingerprint.spec.ts, so a drift on either side
        // fails a test instead of silently disabling duplicate detection.
        ExpenseFingerprint.Compute(Day, 42.50m, "CAD", "Uber Eats")
            .ShouldBe("c47875b9384c326c74638e1329dc036e");
    }

    [Fact]
    public void The_same_transaction_fingerprints_identically()
        => ExpenseFingerprint.Compute(Day, 42.50m, "CAD", "Uber Eats")
            .ShouldBe(ExpenseFingerprint.Compute(Day, 42.50m, "CAD", "Uber Eats"));

    [Fact]
    public void A_different_amount_changes_the_fingerprint()
        => ExpenseFingerprint.Compute(Day, 42.50m, "CAD", "Uber Eats")
            .ShouldNotBe(ExpenseFingerprint.Compute(Day, 42.51m, "CAD", "Uber Eats"));

    [Fact]
    public void A_different_day_changes_the_fingerprint()
        => ExpenseFingerprint.Compute(Day, 42.50m, "CAD", "Uber Eats")
            .ShouldNotBe(ExpenseFingerprint.Compute(Day.AddDays(1), 42.50m, "CAD", "Uber Eats"));

    [Fact]
    public void A_different_currency_changes_the_fingerprint()
        => ExpenseFingerprint.Compute(Day, 42.50m, "CAD", "Uber Eats")
            .ShouldNotBe(ExpenseFingerprint.Compute(Day, 42.50m, "USD", "Uber Eats"));

    [Fact]
    public void The_time_of_day_does_not_change_the_fingerprint()
        => ExpenseFingerprint.Compute(Day, 10m, "CAD", "Coffee")
            .ShouldBe(ExpenseFingerprint.Compute(Day.AddHours(6), 10m, "CAD", "Coffee"));

    [Fact]
    public void Currency_case_does_not_change_the_fingerprint()
        => ExpenseFingerprint.Compute(Day, 10m, "cad", "Coffee")
            .ShouldBe(ExpenseFingerprint.Compute(Day, 10m, "CAD", "Coffee"));

    [Fact]
    public void A_refund_fingerprints_like_the_charge_it_reverses()
        => ExpenseFingerprint.Compute(Day, -10m, "CAD", "Coffee")
            .ShouldBe(ExpenseFingerprint.Compute(Day, 10m, "CAD", "Coffee"));

    [Fact]
    public void A_statement_line_matches_the_hand_typed_expense_it_duplicates()
    {
        // The point of the normalisation: same purchase, two very different strings.
        var statement = ExpenseFingerprint.Compute(Day, 42.50m, "CAD", "UBER EATS 8829 TORONTO ON");
        var manual = ExpenseFingerprint.Compute(Day, 42.50m, "CAD", "Uber Eats");

        statement.ShouldBe(manual);
    }

    [Theory]
    [InlineData("UBER   EATS", "uber eats")]
    [InlineData("Uber-Eats", "UBER EATS")]
    [InlineData("UBER EATS #1234", "Uber Eats")]
    [InlineData("  Uber Eats  ", "Uber Eats")]
    public void Descriptions_normalise_to_the_same_merchant(string left, string right)
        => ExpenseFingerprint.NormalizeDescription(left)
            .ShouldBe(ExpenseFingerprint.NormalizeDescription(right));

    [Fact]
    public void Normalisation_keeps_only_the_leading_merchant_tokens()
        => ExpenseFingerprint.NormalizeDescription("METRO PLUS MARCHE ANDRE TREMBLAY MONTREAL QC")
            .ShouldBe("METRO PLUS");

    [Fact]
    public void Normalisation_strips_long_reference_numbers()
        => ExpenseFingerprint.NormalizeDescription("AMZN MKTP CA 123456789")
            .ShouldBe("AMZN MKTP");

    [Fact]
    public void Merchants_sharing_a_first_word_still_differ()
        => ExpenseFingerprint.Compute(Day, 10m, "CAD", "UBER EATS TORONTO")
            .ShouldNotBe(ExpenseFingerprint.Compute(Day, 10m, "CAD", "UBER TRIP TORONTO"));

    [Fact]
    public void Normalisation_keeps_short_numbers_that_are_part_of_a_name()
        => ExpenseFingerprint.NormalizeDescription("Cafe 22").ShouldBe("CAFE 22");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalisation_of_nothing_is_empty(string input)
        => ExpenseFingerprint.NormalizeDescription(input).ShouldBeEmpty();

    [Fact]
    public void A_blank_description_still_fingerprints()
        => ExpenseFingerprint.Compute(Day, 10m, "CAD", "").Length.ShouldBe(32);

    [Fact]
    public void Different_merchants_do_not_collide()
        => ExpenseFingerprint.Compute(Day, 10m, "CAD", "Metro")
            .ShouldNotBe(ExpenseFingerprint.Compute(Day, 10m, "CAD", "Loblaws"));
}
