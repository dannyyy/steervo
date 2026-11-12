using Steervo.Calculations;
using Steervo.Core;
using Xunit;

namespace Steervo.Tests;

public sealed class ScenarioCalculationTests
{
    private static (Evaluation E, Guid Car, Guid Offer) Setup(int months = 18, decimal rate = 0)
    {
        var e = new Evaluation("owner", "Calculation", null); var car = e.AddCandidate(new("Vehicle"));
        var offer = e.AddOffer(car, new() { Name = "Source", BasePrice = 9999 }, [new("Do not add automatically", PriceKind: OfferPriceKind.Amount, Amount: 876)], "EUR");
        e.SetCostPlanning(new(2027, 10, 1), months); e.SetCalculationSettings(rate); return (e, car, offer);
    }
    private static ScenarioTerms Purchase => new() { Mode = AcquisitionMode.Purchase, PurchasePrice = 1000, InitialFees = 20, CostsReviewed = true };
    private static ScenarioTerms Lease => new() { Mode = AcquisitionMode.LeaseReturn, InitialFees = 10, DownPayment = 40, MonthlyPayment = 50,
        TermMonths = 12, AnnualKilometres = 1100, IncludedKilometresPerYear = 1000, ExcessKilometrePrice = .2m, CostsReviewed = true };
    private static Guid Add(Evaluation e, Guid offer, ScenarioTerms terms) => e.AddScenario(new("Scenario", offer, 1, terms));
    private static void Close(decimal expected, decimal actual) => Assert.InRange(Math.Abs(expected - actual), 0, .000000000001m);

    [Fact]
    public void Cash_lease_and_buyout_use_confirmed_prices_months_and_terminal_values()
    {
        var (e, _, offer) = Setup(); e.AddCost(new("Parking", CostCategory.Parking, CostFrequency.Monthly, 25));
        var cash = Add(e, offer, Purchase); e.SetResidual(cash, new(12, 300, 400, 500));
        var result = ScenarioCalculation.Calculate(e, cash, 12);
        Assert.Equal(920, result.ExpectedCost); Assert.Equal(1020, result.PessimisticCost); Assert.Equal(820, result.OptimisticCost);
        Assert.Equal(1320, result.NetPayments); Assert.Equal(0, result.OpportunityCost); Assert.Equal(-320, result.Account); Assert.Equal(80, result.ExpectedWealth);
        Assert.Equal(1045, result.Months[0].StartExpenses); Assert.Equal(0, result.Months[0].EndExpenses);
        var lease = Add(e, offer, Lease); result = ScenarioCalculation.Calculate(e, lease, 12);
        Assert.Equal(970, result.ExpectedCost); Assert.Equal(20, result.Months[^1].EndExpenses); Assert.Equal(0, result.ExpectedResidual);
        var buyout = Add(e, offer, Lease with { Mode = AcquisitionMode.LeaseBuyout, FinalPayment = 400 });
        e.SetResidual(buyout, new(12, 350, 400, 450)); e.SetResidual(buyout, new(18, 250, 300, 350));
        Assert.Equal(950, ScenarioCalculation.Calculate(e, buyout, 12).ExpectedCost);
        Assert.Equal(1200, ScenarioCalculation.Calculate(e, buyout, 18).ExpectedCost);
        Assert.Null(ScenarioCalculation.Calculate(e, buyout, 15).ExpectedCost);
        Assert.Contains(ScenarioCalculation.Calculate(e, lease, 18).Issues, i => i.Code == "ScenarioLeaseHorizon");
        Assert.Contains(ScenarioCalculation.Calculate(e, buyout, 6).Issues, i => i.Code == "ScenarioLeaseHorizon");
    }
    [Fact]
    public void Effective_annual_return_matches_closed_form_cash_and_annuity_due()
    {
        var (e, _, offer) = Setup(24, 21);
        var cash = Add(e, offer, Purchase with { InitialFees = 0 }); e.SetResidual(cash, new(12, 0, 0, 0));
        var lease = Add(e, offer, Lease with { InitialFees = 0, DownPayment = 0, AnnualKilometres = 1000 });
        var cashResult = ScenarioCalculation.Calculate(e, cash, 12); Close(1210, cashResult.ExpectedCost!.Value); Close(210, cashResult.OpportunityCost);
        var q = (decimal)Math.Pow(1.21, 1d / 12);
        var expected = 50 * q * (.21m / (q - 1));
        Assert.InRange(Math.Abs(expected - ScenarioCalculation.Calculate(e, lease, 12).ExpectedCost!.Value), 0, .000000001m);
        var leaseResult = ScenarioCalculation.Calculate(e, lease, 12);
        Close(cashResult.ExpectedCost.Value - leaseResult.ExpectedCost!.Value, leaseResult.ExpectedWealth!.Value - cashResult.ExpectedWealth!.Value);
        e.SetCalculationSettings(-10); Close(900, ScenarioCalculation.Calculate(e, cash, 12).ExpectedCost!.Value);
        e.SetCalculationSettings(0); Assert.Equal(1000, ScenarioCalculation.Calculate(e, cash, 12).ExpectedCost);
    }
    [Fact]
    public void Credit_accounts_for_month_end_interest_balloon_and_intermediate_debt()
    {
        var (e, _, offer) = Setup(12);
        var credit = Add(e, offer, new() { Mode = AcquisitionMode.Financing, PurchasePrice = 1000, LoanAmount = 1000,
            LoanInterestPercent = 12, TermMonths = 2, MonthlyPayment = 500, FinalPayment = 15.10m, InitialFees = 0, CostsReviewed = true });
        e.SetResidual(credit, new(1, 800, 850, 900)); e.SetResidual(credit, new(2, 700, 750, 800));
        var first = ScenarioCalculation.Calculate(e, credit, 1); Assert.Equal(510, first.Debt); Assert.Equal(160, first.ExpectedCost);
        var result = ScenarioCalculation.Calculate(e, credit, 2); Assert.Equal(1015.10m, result.NetPayments); Assert.Equal(265.10m, result.ExpectedCost); Assert.Equal(0, result.Debt);
        Assert.Equal(0, result.Months[0].StartExpenses); Assert.Equal(500, result.Months[0].EndExpenses); Assert.Equal(515.10m, result.Months[1].EndExpenses);
        e.SetCalculationSettings(21); result = ScenarioCalculation.Calculate(e, credit, 2);
        Close(500 * ScenarioCalculation.MonthlyFactor(21) + 515.10m - 750, result.ExpectedCost!.Value);
        e.EditScenario(credit, e.FindScenario(credit).ToInput() with { Terms = e.FindScenario(credit).Terms with { FinalPayment = 0 } });
        Assert.Contains(ScenarioCalculation.Calculate(e, credit, 2).Issues, i => i.Code == "ScenarioCreditInconsistent");
    }
    [Fact]
    public void Missing_zero_included_adjusted_and_out_of_period_costs_remain_distinct()
    {
        var (e, car, offer) = Setup();
        var cash = Add(e, offer, Purchase); e.SetResidual(cash, new(12, null, 400, null));
        var insurance = e.AddCost(new("Unknown", CostCategory.Insurance, CostFrequency.Yearly, null, 2, CandidateId: car));
        var shared = e.AddCost(new("Monthly", CostCategory.Service, CostFrequency.Monthly, 10));
        e.AddCost(new("Later", CostCategory.Other, CostFrequency.Once, null, 13));
        e.AddCost(new("Included", CostCategory.Other, CostFrequency.Included, null));
        var result = ScenarioCalculation.Calculate(e, cash, 12); Assert.False(result.Complete); Assert.Contains(result.Issues, i => i.Detail == "Unknown"); Assert.False(result.Components.Single(c => c.Name == "Unknown").Complete);
        e.SetCostAdjustment(cash, new(insurance, 0, 2, 2)); e.SetCostAdjustment(cash, new(shared, 0, 1, 6, "Six months included"));
        result = ScenarioCalculation.Calculate(e, cash, 12); Assert.Equal(680, result.ExpectedCost); Assert.Null(result.PessimisticCost); Assert.Null(result.OptimisticCost);
        Assert.Equal(10, e.FindCost(shared).Amount); Assert.Null(e.FindCost(insurance).Amount);
        Assert.False(ScenarioCalculation.Calculate(e, cash, 13).Complete);
        var copy = e.CopyScenario(cash, "Copy"); e.SetCostAdjustment(copy, new(shared, 0, 1, 12));
        Assert.Equal(620, ScenarioCalculation.Calculate(e, copy, 12).ExpectedCost); Assert.Equal(680, ScenarioCalculation.Calculate(e, cash, 12).ExpectedCost);
        e.RemoveCost(insurance); Assert.DoesNotContain(e.FindScenario(cash).CostAdjustments, a => a.CostId == insurance);
    }
    [Fact]
    public void Mileage_prices_are_required_only_for_the_applicable_settlement()
    {
        var (e, _, offer) = Setup(); var lease = Add(e, offer, Lease with { ExcessKilometrePrice = null });
        Assert.Contains(ScenarioCalculation.Calculate(e, lease, 12).Issues, i => i.Code == "ScenarioMissingExcessKilometrePrice");
        e.EditScenario(lease, e.FindScenario(lease).ToInput() with { Terms = Lease with { AnnualKilometres = 900, UnusedKilometreCredit = .1m } });
        Assert.Equal(640, ScenarioCalculation.Calculate(e, lease, 12).ExpectedCost);
        e.EditScenario(lease, e.FindScenario(lease).ToInput() with { Terms = Lease with { AnnualKilometres = 1000, ExcessKilometrePrice = null, UnusedKilometreCredit = null } });
        Assert.Equal(650, ScenarioCalculation.Calculate(e, lease, 12).ExpectedCost);
    }
    [Fact]
    public void Sources_stay_pinned_and_copies_have_independent_residuals()
    {
        var (e, car, offer) = Setup(); var scenario = Add(e, offer, Purchase); e.SetResidual(scenario, new(12, 300, 400, 500));
        e.ReviseOffer(offer, new() { Name = "New price", BasePrice = 20000 }, []);
        Assert.Equal(1, e.FindScenario(scenario).RevisionNumber); Assert.Equal(620, ScenarioCalculation.Calculate(e, scenario, 12).ExpectedCost);
        var copy = e.CopyScenario(scenario, "Copy"); e.SetResidual(copy, new(12, 400, 500, 600));
        Assert.Equal(400, e.FindScenario(scenario).Residuals.Single().Expected);
        var copiedCar = e.CopyCandidate(car, "Another car"); Assert.Equal(2, e.Scenarios.Count); Assert.DoesNotContain(e.Offers, o => o.CandidateId == copiedCar);
        e.RemoveScenario(copy); Assert.Single(e.Scenarios); e.RemoveCandidate(car); Assert.Empty(e.Scenarios);
    }
    [Fact]
    public void Validation_rejects_invalid_bands_scopes_precision_and_returns()
    {
        var (e, _, offer) = Setup(); var scenario = Add(e, offer, Purchase);
        Assert.Throws<RuleException>(() => e.SetResidual(scenario, new(12, 500, 400, 300)));
        Assert.Throws<RuleException>(() => e.SetResidual(scenario, new(0, 0, 0, 0)));
        Assert.Throws<RuleException>(() => Add(e, offer, Purchase with { InitialFees = .001m }));
        Assert.Throws<RuleException>(() => Add(e, offer, Purchase with { Mode = (AcquisitionMode)99 }));
        Assert.Throws<RuleException>(() => e.SetCalculationSettings(-100));
        var otherCar = e.AddCandidate(new("Other")); var cost = e.AddCost(new("Other cost", CostCategory.Other, CostFrequency.Monthly, 5, CandidateId: otherCar));
        Assert.Throws<RuleException>(() => e.SetCostAdjustment(scenario, new(cost, 0)));
        Assert.Throws<RuleException>(() => e.AddScenario(new("Wrong revision", offer, 99, Purchase)));
        Assert.Throws<RuleException>(() => ScenarioCalculation.Calculate(e, scenario, 19));
    }
    [Fact]
    public void Extreme_compounding_fails_explicitly_without_wrapping_or_claiming_zero_cost()
    {
        var (e, _, offer) = Setup(1200, 100); var scenario = Add(e, offer, Purchase);
        var result = ScenarioCalculation.Calculate(e, scenario, 1200);
        Assert.False(result.Complete); Assert.Null(result.Account); Assert.Contains(result.Issues, i => i.Code == "CalculationOverflow");
    }
}
