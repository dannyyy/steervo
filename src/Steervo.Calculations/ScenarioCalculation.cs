using Steervo.Core;

namespace Steervo.Calculations;

public sealed record CalculationIssue(string Code, string? Detail = null);

public sealed record ScenarioMonth(
    int Month,
    decimal StartExpenses,
    decimal StartIncome,
    decimal EndExpenses,
    decimal EndIncome,
    decimal Interest,
    decimal Account,
    decimal Debt
)
{
    public decimal NetPayments => StartExpenses + EndExpenses - StartIncome - EndIncome;
}

public sealed record CoveredCost(string Name, CostCategory Category, int Months);

public sealed record CalculationComponent(
    string Code,
    string? Name,
    decimal NetPayments,
    decimal FuturePayments,
    bool Complete = true
)
{
    public decimal OpportunityCost => FuturePayments - NetPayments;
}

public sealed record ScenarioResult(
    int Month,
    decimal ReferenceCapital,
    decimal NetPayments,
    decimal OpportunityCost,
    decimal Debt,
    decimal? Account,
    decimal? PessimisticCost,
    decimal? ExpectedCost,
    decimal? OptimisticCost,
    decimal? ExpectedWealth,
    decimal? ExpectedResidual,
    IReadOnlyList<CalculationIssue> Issues,
    IReadOnlyList<ScenarioMonth> Months,
    IReadOnlyList<CalculationComponent> Components
)
{
    public bool Complete => ExpectedCost.HasValue;
}

/// <summary>Entry points for projecting what a scenario costs up to a given month.</summary>
public static class ScenarioCalculation
{
    private const int MonthsPerYear = 12;
    private const int MaxRootIterations = 20;
    private const decimal RootTolerance = 0.00000000000000000000000001m;

    // A common reference amount makes account balances comparable. It is not a budget.
    public static decimal ReferenceCapital(Evaluation evaluation)
    {
        return evaluation
            .Scenarios.Select(scenario => scenario.Terms.PurchasePrice ?? 0)
            .DefaultIfEmpty()
            .Max();
    }

    /// <summary>
    /// Projects the scenario up to <paramref name="month"/> of the cost planning period. Missing
    /// input is reported as issues of the result rather than thrown.
    /// </summary>
    public static ScenarioResult Calculate(Evaluation evaluation, Guid scenarioId, int month)
    {
        var scenario = evaluation.FindScenario(scenarioId);
        var plan = evaluation.CostPlanning ?? throw new RuleException("ScenarioPlanningRequired");

        if (month < 1 || month > plan.Months)
        {
            throw new RuleException("InvalidCostPlanning");
        }

        try
        {
            return ScenarioProjection.Run(evaluation, scenario, month);
        }
        catch (OverflowException)
        {
            // Extreme but valid input can exceed the decimal range; report it instead of failing.
            return OverflowResult(evaluation, month);
        }
    }

    /// <summary>
    /// Free benefits of the scenario's offer version that cover a cost category for a stated
    /// number of months.
    /// </summary>
    public static IReadOnlyList<CoveredCost> CoveredCosts(Evaluation evaluation, Scenario scenario)
    {
        var revision = evaluation
            .FindOffer(scenario.OfferId)
            .Revisions.Single(r => r.Number == scenario.RevisionNumber);

        return revision
            .Lines.OrderBy(line => line.Position)
            .Where(CoversCostForFree)
            .Select(line => new CoveredCost(
                line.Name,
                line.CostCategory!.Value,
                line.DurationMonths!.Value
            ))
            .ToArray();
    }

    /// <summary>
    /// Converts an annual return in percent into the equivalent monthly growth factor.
    /// </summary>
    public static decimal MonthlyFactor(decimal annualPercent)
    {
        if (annualPercent is < -99 or > 100)
        {
            throw new RuleException("InvalidReturn");
        }

        var annual = 1 + annualPercent / 100;

        if (annual == 1)
        {
            return 1;
        }

        var value = (decimal)Math.Pow((double)annual, 1d / MonthsPerYear);

        // Newton iteration refines the seed using decimal arithmetic; no monthly money rounding.
        for (var iteration = 0; iteration < MaxRootIterations; iteration++)
        {
            var power = 1m;

            for (var factor = 0; factor < MonthsPerYear - 1; factor++)
            {
                power *= value;
            }

            var next = ((MonthsPerYear - 1) * value + annual / power) / MonthsPerYear;

            if (Math.Abs(next - value) < RootTolerance)
            {
                return next;
            }

            value = next;
        }

        return value;
    }

    private static bool CoversCostForFree(OfferLine line)
    {
        return line
            is {
                Kind: OfferLineKind.Benefit,
                PriceKind: OfferPriceKind.Included,
                Effect: OfferBenefitEffect.CostSaving,
                CostCategory: not null,
                DurationMonths: > 0
            };
    }

    private static ScenarioResult OverflowResult(Evaluation evaluation, int month)
    {
        return new(
            Month: month,
            ReferenceCapital: ReferenceCapital(evaluation),
            NetPayments: 0,
            OpportunityCost: 0,
            Debt: 0,
            Account: null,
            PessimisticCost: null,
            ExpectedCost: null,
            OptimisticCost: null,
            ExpectedWealth: null,
            ExpectedResidual: null,
            Issues: [new("CalculationOverflow")],
            Months: [],
            Components: []
        );
    }
}
