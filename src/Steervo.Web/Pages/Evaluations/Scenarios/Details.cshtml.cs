using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Steervo.Application.Evaluations;
using Steervo.Application.Scenarios;
using Steervo.Calculations;
using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations.Scenarios;

/// <summary>
/// A scenario together with its calculated result, as the result partials expect it.
/// </summary>
public sealed record ScenarioPanel(Evaluation Evaluation, Scenario Scenario, ScenarioResult Result);

/// <summary>
/// Shows one scenario with its result, residual checkpoints, cost adjustments and payment schedule,
/// and handles the inline editors of that page.
/// </summary>
public sealed class DetailsModel(
    ScenarioWorkspace workspace,
    ICurrentUser current,
    IStringLocalizer<Ui> text
) : PageModel
{
    private const string ResidualAction = "residual";
    private const string RemoveResidualAction = "remove-residual";
    private const string AdjustmentAction = "adjustment";
    private const string RemoveAdjustmentAction = "remove-adjustment";
    private const string CopyAction = "copy";

    private const string MonthPeriod = "month";
    private const string YearPeriod = "year";
    private const int PeriodsPerPage = 24;

    private const string ForbiddenError = "Forbidden";
    private const string NotFoundError = "NotFound";
    private const string ConflictError = "Conflict";

    private const string HtmxRequestHeader = "HX-Request";
    private const string HtmxRedirectHeader = "HX-Redirect";

    public Evaluation Evaluation { get; private set; } = null!;

    public Scenario Scenario { get; private set; } = null!;

    public ScenarioResult Result { get; private set; } = null!;

    public AccessLevel Access => Evaluation.AccessFor(current.Id)!.Value;

    public bool CanEdit => Access >= AccessLevel.Editor;

    public Offer Offer => Evaluation.FindOffer(Scenario.OfferId);

    public int Month { get; private set; }

    public string Period { get; private set; } = MonthPeriod;

    public int PageNumber { get; private set; }

    public int PageCount { get; private set; }

    /// <summary>
    /// The rows of the current schedule page. Each entry holds the months of one row: a single
    /// month, or all months of a calendar year.
    /// </summary>
    public IReadOnlyList<ScenarioMonth[]> Periods { get; private set; } = [];

    /// <summary>
    /// The costs an adjustment can refer to: shared costs and those of the scenario's vehicle.
    /// </summary>
    public IReadOnlyList<CostItem> Costs =>
        Evaluation
            .Costs.Where(cost =>
                cost.CandidateId is null
                || cost.CandidateId == Evaluation.FindOffer(Scenario.OfferId).CandidateId
            )
            .OrderBy(cost => cost.Name)
            .ToArray();

    [BindProperty]
    public long Version { get; set; }

    [BindProperty]
    public ResidualForm Residual { get; set; } = new();

    [BindProperty]
    public AdjustmentForm Adjustment { get; set; } = new();

    [BindProperty]
    public string? CopyName { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? LeftId { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? RightId { get; set; }

    public bool EditResidual { get; private set; }

    public bool EditAdjustment { get; private set; }

    public string? FailedAction { get; private set; }

    public string ContextUrl =>
        $"/evaluations/{Evaluation.Id}/scenarios/{Scenario.Id}?month={Month}&period={Period}&pageNumber={PageNumber}&leftId={LeftId}&rightId={RightId}";

    public string? Notice { get; private set; }

    /// <summary>
    /// A leased vehicle that is returned has no residual value for the owner.
    /// </summary>
    public bool KeepsVehicle => Scenario.Terms.Mode != AcquisitionMode.LeaseReturn;

    public bool OfferHasNewerRevision => Offer.Latest.Number != Scenario.RevisionNumber;

    /// <summary>
    /// Amounts that depend on the opportunity calculation stay open while the calculation has issues.
    /// </summary>
    public bool CalculationComplete => Result.Issues.Count == 0;

    public IEnumerable<ResidualCheckpoint> SortedResiduals =>
        Scenario.Residuals.OrderBy(checkpoint => checkpoint.Month);

    public IEnumerable<ScenarioCostAdjustment> SortedAdjustments =>
        Scenario.CostAdjustments.OrderBy(adjustment => Evaluation.FindCost(adjustment.CostId).Name);

    public IReadOnlyList<CoveredCost> CoveredCosts =>
        ScenarioCalculation.CoveredCosts(Evaluation, Scenario);

    public bool ResidualFailed => FailedAction == ResidualAction;

    public bool AdjustmentFailed => FailedAction == AdjustmentAction;

    public bool CopyFailed => FailedAction == CopyAction;

    /// <summary>
    /// Failures of the inline editors are shown inside the editor; everything else at page level.
    /// </summary>
    public bool ShowPageNotice =>
        Notice is not null && !ResidualFailed && !AdjustmentFailed && !CopyFailed;

    public bool ResidualEditorHidden => !EditResidual && !ResidualFailed;

    public bool AdjustmentEditorHidden => !EditAdjustment && !AdjustmentFailed;

    /// <summary>
    /// Id of the control that opened the residual editor; it receives the focus when the editor closes.
    /// </summary>
    public string ResidualEditorOpener =>
        EditResidual ? $"edit-residual-{Residual.Month}" : "open-residual";

    /// <summary>
    /// Id of the control that opened the adjustment editor; it receives the focus when the editor closes.
    /// </summary>
    public string AdjustmentEditorOpener =>
        EditAdjustment ? $"edit-adjustment-{Adjustment.CostId}" : "open-adjustment";

    public string MonthLabel(int month) =>
        Evaluation.CostPlanning!.DateFor(month).ToString("MM.yyyy");

    public string PeriodLabel(ScenarioMonth[] months) =>
        Evaluation
            .CostPlanning!.DateFor(months[0].Month)
            .ToString(Period == YearPeriod ? "yyyy" : "MM.yyyy");

    public static decimal StartPayments(ScenarioMonth[] months) =>
        months.Sum(month => month.StartExpenses - month.StartIncome);

    public static decimal EndPayments(ScenarioMonth[] months) =>
        months.Sum(month => month.EndExpenses - month.EndIncome);

    public static decimal Interest(ScenarioMonth[] months) => months.Sum(month => month.Interest);

    public async Task<IActionResult> OnGetAsync(
        Guid id,
        Guid scenarioId,
        int? month,
        int? residualMonth,
        Guid? costId,
        string? period,
        int pageNumber = 1
    )
    {
        try
        {
            await LoadAsync(id, scenarioId, month, period, pageNumber);
            Version = Evaluation.Version;
            EditResidual = residualMonth.HasValue;
            EditAdjustment = costId.HasValue;

            PrefillResidual(residualMonth ?? Month);

            if (costId.HasValue)
            {
                PrefillAdjustment(costId.Value);
            }

            return Page();
        }
        catch (RuleException error)
        {
            return StatusCode(error.Message == ForbiddenError ? 403 : 404);
        }
    }

    public async Task<IActionResult> OnPostAsync(
        Guid id,
        Guid scenarioId,
        int? month,
        string? period,
        string action,
        int residualMonth,
        Guid costId,
        int pageNumber = 1
    )
    {
        try
        {
            await LoadAsync(id, scenarioId, month, period, pageNumber);
            Evaluation.Require(current.Id, AccessLevel.Editor);

            if (!ModelState.IsValid)
            {
                throw new RuleException("InvalidScenario");
            }

            // A copy opens the new scenario; every other action returns to the section it changed.
            if (action == CopyAction)
            {
                var copyId = await workspace.CopyAsync(id, Version, scenarioId, CopyName ?? "");

                return RedirectTo(CopyUrl(id, copyId, month, period));
            }

            await ApplyAsync(action, id, scenarioId, residualMonth, costId);

            var changedSection = action.Contains(AdjustmentAction)
                ? "#scenario-adjustments"
                : "#scenario-residuals";

            return RedirectTo(ContextUrl + changedSection);
        }
        catch (RuleException error)
        {
            return Failure(error, action);
        }
    }

    private void PrefillResidual(int checkpointMonth)
    {
        var checkpoint = Scenario.Residuals.SingleOrDefault(residual =>
            residual.Month == checkpointMonth
        );

        if (checkpoint is null)
        {
            Residual = new() { Month = checkpointMonth.ToString(CultureInfo.InvariantCulture) };
        }
        else
        {
            Residual = ResidualForm.From(checkpoint);
        }
    }

    private void PrefillAdjustment(Guid costId)
    {
        var adjustment = Scenario.CostAdjustments.SingleOrDefault(candidate =>
            candidate.CostId == costId
        );

        if (adjustment is not null)
        {
            Adjustment = AdjustmentForm.From(adjustment);
        }
    }

    private async Task ApplyAsync(
        string action,
        Guid id,
        Guid scenarioId,
        int residualMonth,
        Guid costId
    )
    {
        switch (action)
        {
            case ResidualAction:
                await workspace.ResidualAsync(id, Version, scenarioId, Residual.ToInput());
                break;

            case RemoveResidualAction:
                await workspace.RemoveResidualAsync(id, Version, scenarioId, residualMonth);
                break;

            case AdjustmentAction:
                await workspace.AdjustmentAsync(id, Version, scenarioId, Adjustment.ToInput());
                break;

            case RemoveAdjustmentAction:
                await workspace.RemoveAdjustmentAsync(id, Version, scenarioId, costId);
                break;

            default:
                throw new RuleException("InvalidValue");
        }
    }

    private string CopyUrl(Guid id, Guid copyId, int? month, string? period) =>
        Url.Page(
            "Details",
            null,
            new
            {
                id,
                scenarioId = copyId,
                month,
                period,
            },
            null,
            null,
            "scenario-title"
        )!;

    /// <summary>
    /// HTMX follows the redirect itself only when it is announced in a response header.
    /// </summary>
    private IActionResult RedirectTo(string url)
    {
        if (Request.Headers.ContainsKey(HtmxRequestHeader))
        {
            Response.Headers[HtmxRedirectHeader] = url;

            return new EmptyResult();
        }

        return Redirect(url);
    }

    private IActionResult Failure(RuleException error, string action)
    {
        if (error.Message is ForbiddenError or NotFoundError)
        {
            return StatusCode(error.Message == ForbiddenError ? 403 : 404);
        }

        FailedAction = action;
        Notice = text[error.Message];
        Response.StatusCode = error.Message == ConflictError ? 409 : 422;

        if (Request.Headers.ContainsKey(HtmxRequestHeader))
        {
            return new JsonResult(
                new
                {
                    error = Notice,
                    version = Evaluation.Version,
                    reviewUrl = ContextUrl,
                    context = Scenario.Name,
                }
            );
        }

        return Page();
    }

    private async Task LoadAsync(
        Guid id,
        Guid scenarioId,
        int? month,
        string? period,
        int pageNumber
    )
    {
        Evaluation = await workspace.ReadAsync(id);
        Scenario = Evaluation.FindScenario(scenarioId);

        // The comparison sides travel along in the query string; sides that no longer exist are dropped.
        LeftId = ScenarioExists(LeftId) ? LeftId : scenarioId;
        RightId = ScenarioExists(RightId) ? RightId : null;

        Month = month ?? Evaluation.CostPlanning!.Months;
        Result = ScenarioCalculation.Calculate(Evaluation, scenarioId, Month);
        Period = period == YearPeriod ? YearPeriod : MonthPeriod;

        var periods = GroupIntoPeriods();
        PageCount = Math.Max(1, (periods.Length + PeriodsPerPage - 1) / PeriodsPerPage);
        PageNumber = Math.Clamp(pageNumber, 1, PageCount);
        Periods = periods.Skip((PageNumber - 1) * PeriodsPerPage).Take(PeriodsPerPage).ToArray();
    }

    private bool ScenarioExists(Guid? scenarioId) =>
        Evaluation.Scenarios.Any(scenario => scenario.Id == scenarioId);

    private ScenarioMonth[][] GroupIntoPeriods()
    {
        if (Period == YearPeriod)
        {
            return Result
                .Months.GroupBy(month => Evaluation.CostPlanning!.DateFor(month.Month).Year)
                .Select(year => year.ToArray())
                .ToArray();
        }

        return Result.Months.Select(month => new[] { month }).ToArray();
    }
}
