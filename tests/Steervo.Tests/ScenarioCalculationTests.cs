using Steervo.Calculations;
using Steervo.Core;
using Xunit;

namespace Steervo.Tests;

/// <summary>
/// Checks the scenario cost calculation against independently derived figures for cash purchase,
/// leasing, buyout and financing.
/// </summary>
public sealed class ScenarioCalculationTests
{
    private const string LeaseHorizonIssue = "ScenarioLeaseHorizon";

    private static ScenarioTerms PurchaseTerms =>
        new()
        {
            Mode = AcquisitionMode.Purchase,
            PurchasePrice = 1000,
            InitialFees = 20,
            CostsReviewed = true,
        };

    private static ScenarioTerms LeaseTerms =>
        new()
        {
            Mode = AcquisitionMode.LeaseReturn,
            InitialFees = 10,
            DownPayment = 40,
            MonthlyPayment = 50,
            TermMonths = 12,
            AnnualKilometres = 1100,
            IncludedKilometresPerYear = 1000,
            ExcessKilometrePrice = .2m,
            CostsReviewed = true,
        };

    [Fact]
    public void Cash_lease_and_buyout_use_confirmed_prices_months_and_terminal_values()
    {
        var (evaluation, _, offer) = CreateEvaluationWithOffer();
        evaluation.AddCost(new("Parking", CostCategory.Parking, CostFrequency.Monthly, 25));

        // Cash purchase: 1000 price + 20 fees + 12 x 25 parking = 1320 paid; the residual band
        // of 300 / 400 / 500 is deducted from that.
        var cash = AddScenario(evaluation, offer, PurchaseTerms);
        evaluation.SetResidual(cash, new(12, 300, 400, 500));

        var cashResult = ScenarioCalculation.Calculate(evaluation, cash, 12);

        Assert.Equal(920, cashResult.ExpectedCost);
        Assert.Equal(1020, cashResult.PessimisticCost);
        Assert.Equal(820, cashResult.OptimisticCost);
        Assert.Equal(1320, cashResult.NetPayments);
        Assert.Equal(0, cashResult.OpportunityCost);
        Assert.Equal(-320, cashResult.Account);
        Assert.Equal(80, cashResult.ExpectedWealth);
        Assert.Equal(1045, cashResult.Months[0].StartExpenses);
        Assert.Equal(0, cashResult.Months[0].EndExpenses);

        // Lease with return: 10 fees + 40 down payment + 12 x 50 rates + 12 x 25 parking = 950,
        // plus 100 excess kilometres at 0.20 settled at the end of the last month.
        var lease = AddScenario(evaluation, offer, LeaseTerms);

        var leaseResult = ScenarioCalculation.Calculate(evaluation, lease, 12);

        Assert.Equal(970, leaseResult.ExpectedCost);
        Assert.Equal(20, leaseResult.Months[^1].EndExpenses);
        Assert.Equal(0, leaseResult.ExpectedResidual);

        // Lease with buyout: the final payment of 400 buys the car, so a residual value is needed
        // for every horizon from the end of the term onwards.
        var buyoutTerms = LeaseTerms with
        {
            Mode = AcquisitionMode.LeaseBuyout,
            FinalPayment = 400,
        };
        var buyout = AddScenario(evaluation, offer, buyoutTerms);
        evaluation.SetResidual(buyout, new(12, 350, 400, 450));
        evaluation.SetResidual(buyout, new(18, 250, 300, 350));

        Assert.Equal(950, ScenarioCalculation.Calculate(evaluation, buyout, 12).ExpectedCost);
        Assert.Equal(1200, ScenarioCalculation.Calculate(evaluation, buyout, 18).ExpectedCost);
        Assert.Null(ScenarioCalculation.Calculate(evaluation, buyout, 15).ExpectedCost);

        // A returned lease cannot be evaluated beyond its term, a buyout not before it.
        Assert.Contains(ScenarioCalculation.Calculate(evaluation, lease, 18).Issues, issue =>
            issue.Code == LeaseHorizonIssue
        );
        Assert.Contains(ScenarioCalculation.Calculate(evaluation, buyout, 6).Issues, issue =>
            issue.Code == LeaseHorizonIssue
        );
    }

    [Fact]
    public void Effective_annual_return_matches_closed_form_cash_and_annuity_due()
    {
        var (evaluation, _, offer) = CreateEvaluationWithOffer(
            planningMonths: 24,
            annualReturn: 21
        );
        var cash = AddScenario(evaluation, offer, PurchaseTerms with { InitialFees = 0 });
        evaluation.SetResidual(cash, new(12, 0, 0, 0));
        var leaseWithoutExtras = LeaseTerms with
        {
            InitialFees = 0,
            DownPayment = 0,
            AnnualKilometres = 1000,
        };
        var lease = AddScenario(evaluation, offer, leaseWithoutExtras);

        // 1000 paid at the start and compounded for one year at 21 %.
        var cashResult = ScenarioCalculation.Calculate(evaluation, cash, 12);

        AssertClose(1210, cashResult.ExpectedCost!.Value);
        AssertClose(210, cashResult.OpportunityCost);

        // Twelve rates of 50 paid at the start of each month are an annuity due.
        var monthlyFactor = (decimal)Math.Pow(1.21, 1d / 12);
        var annuityDue = 50 * monthlyFactor * (.21m / (monthlyFactor - 1));
        Assert.InRange(
            Math.Abs(
                annuityDue
                    - ScenarioCalculation.Calculate(evaluation, lease, 12).ExpectedCost!.Value
            ),
            0,
            .000000001m
        );

        // Whatever one scenario costs more than the other, it ends with exactly that much less wealth.
        var leaseResult = ScenarioCalculation.Calculate(evaluation, lease, 12);
        AssertClose(
            cashResult.ExpectedCost.Value - leaseResult.ExpectedCost!.Value,
            leaseResult.ExpectedWealth!.Value - cashResult.ExpectedWealth!.Value
        );

        // A negative return shrinks the cost of capital, a return of zero removes it.
        evaluation.SetCalculationSettings(-10);
        AssertClose(900, ScenarioCalculation.Calculate(evaluation, cash, 12).ExpectedCost!.Value);

        evaluation.SetCalculationSettings(0);
        Assert.Equal(1000, ScenarioCalculation.Calculate(evaluation, cash, 12).ExpectedCost);
    }

    [Fact]
    public void Credit_accounts_for_month_end_interest_balloon_and_intermediate_debt()
    {
        var (evaluation, _, offer) = CreateEvaluationWithOffer(planningMonths: 12);
        var creditTerms = new ScenarioTerms
        {
            Mode = AcquisitionMode.Financing,
            PurchasePrice = 1000,
            LoanAmount = 1000,
            LoanInterestPercent = 12,
            TermMonths = 2,
            MonthlyPayment = 500,
            FinalPayment = 15.10m,
            InitialFees = 0,
            CostsReviewed = true,
        };
        var credit = AddScenario(evaluation, offer, creditTerms);
        evaluation.SetResidual(credit, new(1, 800, 850, 900));
        evaluation.SetResidual(credit, new(2, 700, 750, 800));

        // After one month: 1000 + 1 % interest - 500 rate = 510 still owed;
        // cost = 500 paid + 510 debt - 850 residual.
        var afterFirstMonth = ScenarioCalculation.Calculate(evaluation, credit, 1);

        Assert.Equal(510, afterFirstMonth.Debt);
        Assert.Equal(160, afterFirstMonth.ExpectedCost);

        // After two months: 510 + 1 % interest = 515.10, settled by the second rate and the balloon.
        var afterTerm = ScenarioCalculation.Calculate(evaluation, credit, 2);

        Assert.Equal(1015.10m, afterTerm.NetPayments);
        Assert.Equal(265.10m, afterTerm.ExpectedCost);
        Assert.Equal(0, afterTerm.Debt);
        Assert.Equal(0, afterTerm.Months[0].StartExpenses);
        Assert.Equal(500, afterTerm.Months[0].EndExpenses);
        Assert.Equal(515.10m, afterTerm.Months[1].EndExpenses);

        // With a return on capital the first rate is compounded for the remaining month.
        evaluation.SetCalculationSettings(21);
        var withReturn = ScenarioCalculation.Calculate(evaluation, credit, 2);

        AssertClose(
            500 * ScenarioCalculation.MonthlyFactor(21) + 515.10m - 750,
            withReturn.ExpectedCost!.Value
        );

        // Without the balloon the payments no longer repay the loan.
        evaluation.EditScenario(
            credit,
            evaluation.FindScenario(credit).ToInput() with
            {
                Terms = evaluation.FindScenario(credit).Terms with { FinalPayment = 0 },
            }
        );

        Assert.Contains(ScenarioCalculation.Calculate(evaluation, credit, 2).Issues, issue =>
            issue.Code == "ScenarioCreditInconsistent"
        );
    }

    [Fact]
    public void Missing_zero_included_adjusted_and_out_of_period_costs_remain_distinct()
    {
        var (evaluation, car, offer) = CreateEvaluationWithOffer();
        var cash = AddScenario(evaluation, offer, PurchaseTerms);
        evaluation.SetResidual(cash, new(12, null, 400, null));
        var insurance = evaluation.AddCost(
            new("Unknown", CostCategory.Insurance, CostFrequency.Yearly, null, 2, CandidateId: car)
        );
        var shared = evaluation.AddCost(
            new("Monthly", CostCategory.Service, CostFrequency.Monthly, 10)
        );
        evaluation.AddCost(new("Later", CostCategory.Other, CostFrequency.Once, null, 13));
        evaluation.AddCost(new("Included", CostCategory.Other, CostFrequency.Included, null));

        // A cost without an amount inside the horizon makes the result incomplete.
        var incomplete = ScenarioCalculation.Calculate(evaluation, cash, 12);

        Assert.False(incomplete.Complete);
        Assert.Contains(incomplete.Issues, issue => issue.Detail == "Unknown");
        Assert.False(incomplete.Components.Single(c => c.Name == "Unknown").Complete);

        // Scenario adjustments settle the unknown insurance at zero and waive six months of the
        // shared cost: 1000 + 20 + 6 x 10 - 400 residual = 680.
        evaluation.SetCostAdjustment(cash, new(insurance, 0, 2, 2));
        evaluation.SetCostAdjustment(cash, new(shared, 0, 1, 6, "Six months included"));

        var adjusted = ScenarioCalculation.Calculate(evaluation, cash, 12);

        Assert.Equal(680, adjusted.ExpectedCost);
        Assert.Null(adjusted.PessimisticCost);
        Assert.Null(adjusted.OptimisticCost);

        // The adjustments belong to the scenario; the cost items themselves stay untouched.
        Assert.Equal(10, evaluation.FindCost(shared).Amount);
        Assert.Null(evaluation.FindCost(insurance).Amount);

        // Month 13 brings the unknown one-off cost into the horizon.
        Assert.False(ScenarioCalculation.Calculate(evaluation, cash, 13).Complete);

        // A copy can be adjusted further without changing the original scenario.
        var copy = evaluation.CopyScenario(cash, "Copy");
        evaluation.SetCostAdjustment(copy, new(shared, 0, 1, 12));

        Assert.Equal(620, ScenarioCalculation.Calculate(evaluation, copy, 12).ExpectedCost);
        Assert.Equal(680, ScenarioCalculation.Calculate(evaluation, cash, 12).ExpectedCost);

        // Removing a cost removes the adjustments that referred to it.
        evaluation.RemoveCost(insurance);

        Assert.DoesNotContain(evaluation.FindScenario(cash).CostAdjustments, adjustment =>
            adjustment.CostId == insurance
        );
    }

    [Fact]
    public void Free_benefits_of_the_offer_cover_matching_costs_for_their_duration()
    {
        var (evaluation, _, offer) = CreateEvaluationWithOffer();
        var service = evaluation.AddCost(
            new("Service", CostCategory.Service, CostFrequency.Monthly, 10)
        );
        evaluation.AddCost(new("Tyres", CostCategory.Tires, CostFrequency.Monthly, 5));
        evaluation.AddCost(
            new(
                "Service refund",
                CostCategory.Service,
                CostFrequency.Once,
                30,
                Direction: CostDirection.Income
            )
        );
        var plain = AddScenario(evaluation, offer, PurchaseTerms);
        evaluation.SetResidual(plain, new(12, null, 400, null));

        var costWithoutBenefits = ScenarioCalculation
            .Calculate(evaluation, plain, 12)
            .ExpectedCost!.Value;

        Assert.Empty(ScenarioCalculation.CoveredCosts(evaluation, evaluation.FindScenario(plain)));

        // A new offer version includes six months of service; only scenarios on that version benefit.
        evaluation.ReviseOffer(
            offer,
            evaluation.FindOffer(offer).Latest.Terms,
            [
                new(
                    "Service pack",
                    OfferLineKind.Benefit,
                    OfferPriceKind.Included,
                    Effect: OfferBenefitEffect.CostSaving,
                    CostCategory: CostCategory.Service,
                    DurationMonths: 6
                ),
                new(
                    "Open ended",
                    OfferLineKind.Benefit,
                    OfferPriceKind.Included,
                    Effect: OfferBenefitEffect.CostSaving,
                    CostCategory: CostCategory.Tires
                ),
                new(
                    "Priority",
                    OfferLineKind.Benefit,
                    OfferPriceKind.Included,
                    Effect: OfferBenefitEffect.Right,
                    DurationMonths: 12
                ),
            ],
            "Service included"
        );
        var withPack = evaluation.AddScenario(new("With pack", offer, 2, PurchaseTerms));
        evaluation.SetResidual(withPack, new(12, null, 400, null));

        Assert.Equal(
            costWithoutBenefits,
            ScenarioCalculation.Calculate(evaluation, plain, 12).ExpectedCost
        );
        Assert.Equal(
            costWithoutBenefits - 60,
            ScenarioCalculation.Calculate(evaluation, withPack, 12).ExpectedCost
        );
        Assert.Equal(
            [new CoveredCost("Service pack", CostCategory.Service, 6)],
            ScenarioCalculation.CoveredCosts(evaluation, evaluation.FindScenario(withPack))
        );

        // An explicit adjustment of the scenario takes precedence over the automatic cover for that cost.
        evaluation.SetCostAdjustment(withPack, new(service, 4, 1, 3));

        Assert.Equal(
            costWithoutBenefits - 3 * 6,
            ScenarioCalculation.Calculate(evaluation, withPack, 12).ExpectedCost
        );
        Assert.Equal(10, evaluation.FindCost(service).Amount);
    }

    [Fact]
    public void Mileage_prices_are_required_only_for_the_applicable_settlement()
    {
        var (evaluation, _, offer) = CreateEvaluationWithOffer();

        // Driving more than included requires a price for the excess kilometres.
        var lease = AddScenario(evaluation, offer, LeaseTerms with { ExcessKilometrePrice = null });

        Assert.Contains(ScenarioCalculation.Calculate(evaluation, lease, 12).Issues, issue =>
            issue.Code == "ScenarioMissingExcessKilometrePrice"
        );

        // Driving 100 kilometres less earns the credit: 650 - 100 x 0.10.
        evaluation.EditScenario(
            lease,
            evaluation.FindScenario(lease).ToInput() with
            {
                Terms = LeaseTerms with { AnnualKilometres = 900, UnusedKilometreCredit = .1m },
            }
        );

        Assert.Equal(640, ScenarioCalculation.Calculate(evaluation, lease, 12).ExpectedCost);

        // Driving exactly the included distance needs neither price.
        evaluation.EditScenario(
            lease,
            evaluation.FindScenario(lease).ToInput() with
            {
                Terms = LeaseTerms with
                {
                    AnnualKilometres = 1000,
                    ExcessKilometrePrice = null,
                    UnusedKilometreCredit = null,
                },
            }
        );

        Assert.Equal(650, ScenarioCalculation.Calculate(evaluation, lease, 12).ExpectedCost);
    }

    [Fact]
    public void Sources_stay_pinned_and_copies_have_independent_residuals()
    {
        var (evaluation, car, offer) = CreateEvaluationWithOffer();
        var scenario = AddScenario(evaluation, offer, PurchaseTerms);
        evaluation.SetResidual(scenario, new(12, 300, 400, 500));

        // A later offer version does not move the scenario away from the version it was built on.
        evaluation.ReviseOffer(offer, new() { Name = "New price", StatedTotal = 20000 }, []);

        Assert.Equal(1, evaluation.FindScenario(scenario).RevisionNumber);
        Assert.Equal(620, ScenarioCalculation.Calculate(evaluation, scenario, 12).ExpectedCost);

        // Residual values of a copied scenario are its own.
        var copy = evaluation.CopyScenario(scenario, "Copy");
        evaluation.SetResidual(copy, new(12, 400, 500, 600));

        Assert.Equal(400, evaluation.FindScenario(scenario).Residuals.Single().Expected);

        // Copying a car copies neither its offers nor its scenarios.
        var copiedCar = evaluation.CopyCandidate(car, "Another car");

        Assert.Equal(2, evaluation.Scenarios.Count);
        Assert.DoesNotContain(evaluation.Offers, o => o.CandidateId == copiedCar);

        evaluation.RemoveScenario(copy);
        Assert.Single(evaluation.Scenarios);

        evaluation.RemoveCandidate(car);
        Assert.Empty(evaluation.Scenarios);
    }

    [Fact]
    public void Validation_rejects_invalid_bands_scopes_precision_and_returns()
    {
        var (evaluation, _, offer) = CreateEvaluationWithOffer();
        var scenario = AddScenario(evaluation, offer, PurchaseTerms);

        // Residual bands must be ordered and belong to a month of the plan.
        Assert.Throws<RuleException>(() =>
            evaluation.SetResidual(scenario, new(12, 500, 400, 300))
        );
        Assert.Throws<RuleException>(() => evaluation.SetResidual(scenario, new(0, 0, 0, 0)));

        // Amounts have two decimals at most, and the acquisition mode must be a known one.
        Assert.Throws<RuleException>(() =>
            AddScenario(evaluation, offer, PurchaseTerms with { InitialFees = .001m })
        );
        Assert.Throws<RuleException>(() =>
            AddScenario(evaluation, offer, PurchaseTerms with { Mode = (AcquisitionMode)99 })
        );
        Assert.Throws<RuleException>(() => evaluation.SetCalculationSettings(-100));

        // A cost of another car cannot be adjusted in this scenario.
        var otherCar = evaluation.AddCandidate(new("Other"));
        var costOfOtherCar = evaluation.AddCost(
            new("Other cost", CostCategory.Other, CostFrequency.Monthly, 5, CandidateId: otherCar)
        );
        Assert.Throws<RuleException>(() =>
            evaluation.SetCostAdjustment(scenario, new(costOfOtherCar, 0))
        );

        // The offer version and the horizon must exist.
        Assert.Throws<RuleException>(() =>
            evaluation.AddScenario(new("Wrong revision", offer, 99, PurchaseTerms))
        );
        Assert.Throws<RuleException>(() => ScenarioCalculation.Calculate(evaluation, scenario, 19));
    }

    [Fact]
    public void Extreme_compounding_fails_explicitly_without_wrapping_or_claiming_zero_cost()
    {
        var (evaluation, _, offer) = CreateEvaluationWithOffer(
            planningMonths: 1200,
            annualReturn: 100
        );
        var scenario = AddScenario(evaluation, offer, PurchaseTerms);

        var result = ScenarioCalculation.Calculate(evaluation, scenario, 1200);

        Assert.False(result.Complete);
        Assert.Null(result.Account);
        Assert.Contains(result.Issues, issue => issue.Code == "CalculationOverflow");
    }

    /// <summary>
    /// An evaluation with one car and one offer. The offer deliberately states a total and a
    /// priced line that no scenario may pick up on its own.
    /// </summary>
    private static (Evaluation Evaluation, Guid Car, Guid Offer) CreateEvaluationWithOffer(
        int planningMonths = 18,
        decimal annualReturn = 0
    )
    {
        var evaluation = new Evaluation("owner", "Calculation", null);
        var car = evaluation.AddCandidate(new("Vehicle"));
        var offer = evaluation.AddOffer(
            car,
            new() { Name = "Source", StatedTotal = 9999 },
            [new("Do not add automatically", PriceKind: OfferPriceKind.Amount, Amount: 876)],
            "EUR"
        );

        evaluation.SetCostPlanning(new(2027, 10, 1), planningMonths);
        evaluation.SetCalculationSettings(annualReturn);

        return (evaluation, car, offer);
    }

    private static Guid AddScenario(Evaluation evaluation, Guid offer, ScenarioTerms terms)
    {
        return evaluation.AddScenario(new("Scenario", offer, 1, terms));
    }

    private static void AssertClose(decimal expected, decimal actual)
    {
        Assert.InRange(Math.Abs(expected - actual), 0, .000000000001m);
    }
}
