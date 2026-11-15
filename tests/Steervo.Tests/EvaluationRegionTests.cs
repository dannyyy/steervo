using Steervo.Core;
using Xunit;

namespace Steervo.Tests;

public sealed class EvaluationRegionTests
{
    [Fact]
    public void Every_selectable_country_has_a_display_name()
    {
        foreach (var code in EvaluationCountries.Codes) Assert.False(string.IsNullOrWhiteSpace(EvaluationCountries.DisplayName(code)));
    }

    [Theory]
    [InlineData(null, "EUR", "CountryRequired")]
    [InlineData("", "EUR", "CountryRequired")]
    [InlineData("ZZ", "EUR", "CountryRequired")]
    [InlineData("CHE", "CHF", "CountryRequired")]
    [InlineData("CH", null, "CurrencyRequired")]
    [InlineData("CH", "12$", "CurrencyRequired")]
    public void Properties_require_country_and_currency_without_partial_changes(string? country, string? currency, string error)
    {
        var e = new Evaluation("owner", "Original", "Purpose");
        e.SetProperties(e.Name, e.Purpose, "DE", "EUR");
        Assert.Equal(error, Assert.Throws<RuleException>(() => e.SetProperties("Changed", null, country, currency)).Message);
        Assert.Equal("Original", e.Name); Assert.Equal("Purpose", e.Purpose);
        Assert.Equal("DE", e.Country); Assert.Equal("EUR", e.Currency);
    }

    [Fact]
    public void Currency_is_independent_of_country_and_can_be_corrected_before_financial_records()
    {
        var e = new Evaluation("owner", "Decision", null);
        Assert.False(e.HasRegionalSettings); Assert.Null(e.Country);
        e.SetProperties(e.Name, null, " ch ", " eur ");
        Assert.Equal("CH", e.Country); Assert.Equal("EUR", e.Currency);
        Assert.True(e.HasRegionalSettings); Assert.False(e.IsCurrencyFixed);
        e.SetProperties(e.Name, null, "LI", "CHF");
        Assert.Equal("LI", e.Country); Assert.Equal("CHF", e.Currency);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Currency_is_locked_by_costs_or_offers_while_country_remains_editable(bool offer)
    {
        var e = new Evaluation("owner", "Decision", null);
        e.SetProperties(e.Name, null, "CH", "CHF");
        if (offer) e.AddOffer(e.AddCandidate(new("Car")), new() { Name = "Offer", BasePrice = 1000 }, []);
        else { e.SetCostPlanning(new(2026, 1, 1), 12); e.AddCost(new("Parking", CostCategory.Other, Frequency: CostFrequency.Monthly, Amount: 100)); }
        Assert.True(e.IsCurrencyFixed);
        Assert.Equal("CurrencyFixed", Assert.Throws<RuleException>(() => e.SetProperties("Changed", null, "DE", "EUR")).Message);
        Assert.Equal("Decision", e.Name); Assert.Equal("CH", e.Country); Assert.Equal("CHF", e.Currency);
        e.SetProperties("Changed", null, "DE", "CHF");
        Assert.Equal("DE", e.Country); Assert.Equal("CHF", e.Currency);
    }
}
