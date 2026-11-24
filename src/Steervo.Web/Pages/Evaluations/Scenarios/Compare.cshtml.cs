using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Steervo.Application.Evaluations;
using Steervo.Application.Scenarios;
using Steervo.Calculations;
using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations.Scenarios;

/// <summary>
/// One row of the comparison table: the resource key of its label and the amount it shows per scenario.
/// </summary>
public sealed record ComparisonRow(string Label, Func<ScenarioPanel, decimal?> Value);

/// <summary>
/// Compares two scenarios of an evaluation side by side at one month of the planning period.
/// </summary>
public sealed class CompareModel(ScenarioWorkspace workspace, ICurrentUser current) : PageModel
{
    private const string ForbiddenError = "Forbidden";
    private const string NotFoundError = "NotFound";

    // Below half a cent both amounts are displayed identically, so the scenarios count as equally expensive.
    private const decimal EqualCostTolerance = .005m;

    /// <summary>
    /// Amounts that depend on the opportunity calculation stay open while the calculation has issues.
    /// </summary>
    public static IReadOnlyList<ComparisonRow> CostRows { get; } =
    [
        new(
            "CalculationNetPayments",
            panel => panel.Result.Months.Count == 0 ? null : panel.Result.NetPayments
        ),
        new(
            "CalculationOpportunity",
            panel => panel.Result.Issues.Count == 0 ? panel.Result.OpportunityCost : null
        ),
        new(
            "CalculationRemainingDebt",
            panel => panel.Result.Issues.Count == 0 ? panel.Result.Debt : null
        ),
        new("CalculationResidualDeduction", panel => panel.Result.ExpectedResidual),
    ];

    public static IReadOnlyList<ComparisonRow> ResidualBandRows { get; } =
    [
        new("ResidualLow", panel => panel.Result.PessimisticCost),
        new("ResidualExpected", panel => panel.Result.ExpectedCost),
        new("ResidualHigh", panel => panel.Result.OptimisticCost),
    ];

    public static IReadOnlyList<ComparisonRow> WealthRows { get; } =
    [
        new("CalculationReferenceCapital", panel => panel.Result.ReferenceCapital),
        new("CalculationAccount", panel => panel.Result.Account),
        new("CalculationFinalWealth", panel => panel.Result.ExpectedWealth),
    ];

    public Evaluation Evaluation { get; private set; } = null!;

    public AccessLevel Access => Evaluation.AccessFor(current.Id)!.Value;

    public bool CanEdit => Access >= AccessLevel.Editor;

    public Guid? LeftId { get; private set; }

    public Guid? RightId { get; private set; }

    public int Month { get; private set; }

    public ScenarioPanel? Left { get; private set; }

    public ScenarioPanel? Right { get; private set; }

    public bool BothSidesChosen => Left is not null && Right is not null;

    /// <summary>
    /// Both scenarios in display order. Only available once both sides are chosen.
    /// </summary>
    public IReadOnlyList<ScenarioPanel> Panels => [Left!, Right!];

    public IEnumerable<Scenario> SortedScenarios =>
        Evaluation.Scenarios.OrderBy(scenario => scenario.Name);

    /// <summary>
    /// A leased vehicle that is returned has no residual value, so the residual band only applies
    /// when at least one scenario keeps the vehicle.
    /// </summary>
    public bool ShowResidualBand => Panels.Any(KeepsVehicle);

    /// <summary>
    /// Absolute difference of the expected costs, or null while one of them is still open.
    /// </summary>
    public decimal? CostDifference =>
        ExpectedCosts is { } costs ? Math.Abs(costs.Left - costs.Right) : null;

    public bool CostsEqual => CostDifference < EqualCostTolerance;

    public decimal MonthlyCostDifference => CostDifference!.Value / Month;

    public string CheaperScenarioName
    {
        get
        {
            var costs = ExpectedCosts!.Value;

            return costs.Left < costs.Right ? Left!.Scenario.Name : Right!.Scenario.Name;
        }
    }

    /// <summary>
    /// The residual value at which the left scenario would cost as much as the right one. Null when
    /// the left scenario has no residual value or both sides show the same scenario.
    /// </summary>
    public decimal? ResidualThreshold
    {
        get
        {
            if (ExpectedCosts is not { } costs)
            {
                return null;
            }

            var differentScenarios = LeftId != RightId;

            if (
                Left!.Result.ExpectedResidual is { } residual
                && KeepsVehicle(Left)
                && differentScenarios
            )
            {
                return residual + costs.Left - costs.Right;
            }

            return null;
        }
    }

    public string ResidualThresholdLabel =>
        ResidualThreshold >= 0 ? "ScenarioResidualThreshold" : "ScenarioResidualThresholdBelowZero";

    private (decimal Left, decimal Right)? ExpectedCosts
    {
        get
        {
            if (
                Left?.Result.ExpectedCost is { } leftCost
                && Right?.Result.ExpectedCost is { } rightCost
            )
            {
                return (leftCost, rightCost);
            }

            return null;
        }
    }

    public async Task<IActionResult> OnGetAsync(Guid id, Guid? leftId, Guid? rightId, int? month)
    {
        try
        {
            Evaluation = await workspace.ReadAsync(id);
            LeftId = leftId;
            RightId = rightId;

            if (Evaluation.CostPlanning is null)
            {
                return RedirectToPage("Index", new { id });
            }

            Month = month ?? Evaluation.CostPlanning.Months;

            var monthOutsidePlanning = Month < 1 || Month > Evaluation.CostPlanning.Months;
            if (monthOutsidePlanning)
            {
                throw new RuleException(NotFoundError);
            }

            if (leftId.HasValue)
            {
                Left = CalculatePanel(leftId.Value);
            }

            if (rightId.HasValue)
            {
                Right = CalculatePanel(rightId.Value);
            }

            return Page();
        }
        catch (RuleException error)
        {
            return StatusCode(error.Message == ForbiddenError ? 403 : 404);
        }
    }

    private static bool KeepsVehicle(ScenarioPanel panel) =>
        panel.Scenario.Terms.Mode != AcquisitionMode.LeaseReturn;

    private ScenarioPanel CalculatePanel(Guid scenarioId) =>
        new(
            Evaluation,
            Evaluation.FindScenario(scenarioId),
            ScenarioCalculation.Calculate(Evaluation, scenarioId, Month)
        );
}
