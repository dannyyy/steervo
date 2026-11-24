using Steervo.Core;
using Xunit;

namespace Steervo.Tests;

/// <summary>
/// Checks the country and currency of an evaluation.
/// </summary>
public sealed class EvaluationRegionTests
{
    [Fact]
    public void Every_selectable_country_has_a_display_name()
    {
        foreach (var code in EvaluationCountries.Codes)
        {
            Assert.False(string.IsNullOrWhiteSpace(EvaluationCountries.DisplayName(code)));
        }
    }

    [Theory]
    [InlineData(null, "EUR", "CountryRequired")]
    [InlineData("", "EUR", "CountryRequired")]
    [InlineData("ZZ", "EUR", "CountryRequired")]
    [InlineData("CHE", "CHF", "CountryRequired")]
    [InlineData("CH", null, "CurrencyRequired")]
    [InlineData("CH", "12$", "CurrencyRequired")]
    public void Properties_require_country_and_currency_without_partial_changes(
        string? country,
        string? currency,
        string error
    )
    {
        var evaluation = new Evaluation("owner", "Original", "Purpose");
        evaluation.SetProperties(evaluation.Name, evaluation.Purpose, "DE", "EUR");

        var rejection = Assert.Throws<RuleException>(() =>
            evaluation.SetProperties("Changed", null, country, currency)
        );

        Assert.Equal(error, rejection.Message);
        Assert.Equal("Original", evaluation.Name);
        Assert.Equal("Purpose", evaluation.Purpose);
        Assert.Equal("DE", evaluation.Country);
        Assert.Equal("EUR", evaluation.Currency);
    }

    [Fact]
    public void Currency_is_independent_of_country_and_can_be_corrected_before_financial_records()
    {
        var evaluation = new Evaluation("owner", "Decision", null);

        Assert.False(evaluation.HasRegionalSettings);
        Assert.Null(evaluation.Country);

        // Codes are trimmed and upper-cased; the currency need not be the one of the country.
        evaluation.SetProperties(evaluation.Name, null, " ch ", " eur ");

        Assert.Equal("CH", evaluation.Country);
        Assert.Equal("EUR", evaluation.Currency);
        Assert.True(evaluation.HasRegionalSettings);
        Assert.False(evaluation.IsCurrencyFixed);

        evaluation.SetProperties(evaluation.Name, null, "LI", "CHF");

        Assert.Equal("LI", evaluation.Country);
        Assert.Equal("CHF", evaluation.Currency);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Currency_is_locked_by_costs_or_offers_while_country_remains_editable(bool offer)
    {
        var evaluation = new Evaluation("owner", "Decision", null);
        evaluation.SetProperties(evaluation.Name, null, "CH", "CHF");

        if (offer)
        {
            evaluation.AddOffer(
                evaluation.AddCandidate(new("Car")),
                new() { Name = "Offer", StatedTotal = 1000 },
                []
            );
        }
        else
        {
            evaluation.SetCostPlanning(new(2026, 1, 1), 12);
            evaluation.AddCost(
                new("Parking", CostCategory.Other, Frequency: CostFrequency.Monthly, Amount: 100)
            );
        }

        Assert.True(evaluation.IsCurrencyFixed);

        // Changing the currency is rejected as a whole, including the other properties.
        var rejection = Assert.Throws<RuleException>(() =>
            evaluation.SetProperties("Changed", null, "DE", "EUR")
        );

        Assert.Equal("CurrencyFixed", rejection.Message);
        Assert.Equal("Decision", evaluation.Name);
        Assert.Equal("CH", evaluation.Country);
        Assert.Equal("CHF", evaluation.Currency);

        // With the same currency the country can still change.
        evaluation.SetProperties("Changed", null, "DE", "CHF");

        Assert.Equal("DE", evaluation.Country);
        Assert.Equal("CHF", evaluation.Currency);
    }
}
