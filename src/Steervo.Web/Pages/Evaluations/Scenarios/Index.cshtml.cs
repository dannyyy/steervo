using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Steervo.Application.Evaluations;
using Steervo.Application.Scenarios;
using Steervo.Calculations;
using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations.Scenarios;

/// <summary>
/// Lists the scenarios of an evaluation and maintains the annual return all of them share.
/// </summary>
public sealed class IndexModel(
    ScenarioWorkspace workspace,
    ICurrentUser current,
    IStringLocalizer<Ui> text
) : PageModel
{
    private const string SettingsAction = "settings";
    private const string RemoveAction = "remove";

    private const string ForbiddenError = "Forbidden";
    private const string NotFoundError = "NotFound";
    private const string ConflictError = "Conflict";

    private const string HtmxRequestHeader = "HX-Request";
    private const string HtmxRedirectHeader = "HX-Redirect";

    public Evaluation Evaluation { get; private set; } = null!;

    public AccessLevel Access => Evaluation.AccessFor(current.Id)!.Value;

    public bool CanEdit => Access >= AccessLevel.Editor;

    [BindProperty]
    public string? AnnualReturn { get; set; }

    [BindProperty]
    public long Version { get; set; }

    public string? FailedAction { get; private set; }

    public string? Notice { get; private set; }

    /// <summary>
    /// Scenarios need the planning period and the currency of the evaluation.
    /// </summary>
    public bool PlanningMissing => Evaluation.CostPlanning is null || Evaluation.Currency is null;

    public bool CanAddScenario => CanEdit && !PlanningMissing;

    public IEnumerable<Scenario> SortedScenarios =>
        Evaluation.Scenarios.OrderBy(scenario => scenario.Name);

    public bool SettingsFailed => FailedAction == SettingsAction;

    /// <summary>
    /// Failures of the settings editor are shown inside the editor; everything else at page level.
    /// </summary>
    public bool ShowPageNotice => Notice is not null && !SettingsFailed;

    /// <summary>
    /// The settings editor starts open while the annual return is still missing or its last save failed.
    /// </summary>
    public bool SettingsEditorOpen => Evaluation.CalculationSettings is null || SettingsFailed;

    public string AnnualReturnLabel
    {
        get
        {
            if (Evaluation.CalculationSettings is null)
            {
                return text["OfferOpen"].Value;
            }

            return Evaluation.CalculationSettings.AnnualReturnPercent.ToString("0.####") + " %";
        }
    }

    /// <summary>
    /// The result at the end of the planning period, or null while there is no planning period.
    /// </summary>
    public ScenarioResult? ResultFor(Scenario scenario)
    {
        if (Evaluation.CostPlanning is { } planning)
        {
            return ScenarioCalculation.Calculate(Evaluation, scenario.Id, planning.Months);
        }

        return null;
    }

    public string ExpectedCostLabel(ScenarioResult result)
    {
        if (result.ExpectedCost is { } amount)
        {
            return $"{amount:N2} {Evaluation.Currency}";
        }

        return text["ScenarioSummaryOpen"].Value;
    }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        try
        {
            Evaluation = await workspace.ReadAsync(id);
            Version = Evaluation.Version;
            AnnualReturn = ScenarioForm.Format(Evaluation.CalculationSettings?.AnnualReturnPercent);

            return Page();
        }
        catch (RuleException error)
        {
            return StatusCode(error.Message == ForbiddenError ? 403 : 404);
        }
    }

    public async Task<IActionResult> OnPostAsync(Guid id, string action, Guid scenarioId)
    {
        try
        {
            Evaluation = await workspace.ReadAsync(id);
            Evaluation.Require(current.Id, AccessLevel.Editor);

            if (!ModelState.IsValid)
            {
                throw new RuleException("InvalidScenario");
            }

            if (action == SettingsAction)
            {
                var annualReturn = ScenarioForm.Number(AnnualReturn)
                    ?? throw new RuleException("InvalidReturn");

                await workspace.SettingsAsync(id, Version, annualReturn);
            }
            else if (action == RemoveAction)
            {
                await workspace.RemoveAsync(id, Version, scenarioId);
            }
            else
            {
                throw new RuleException("InvalidValue");
            }

            var changedSection =
                action == SettingsAction ? "#scenario-settings-title" : "#scenario-list-title";

            return RedirectTo($"/evaluations/{id}/scenarios" + changedSection);
        }
        catch (RuleException error)
        {
            return Failure(error, id, action, scenarioId);
        }
    }

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

    private IActionResult Failure(RuleException error, Guid id, string action, Guid scenarioId)
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
                    reviewUrl = $"/evaluations/{id}/scenarios",
                    context = FailureContext(action, scenarioId),
                }
            );
        }

        return Page();
    }

    /// <summary>
    /// Names what the failed action referred to: the scenario to remove, or the current annual return.
    /// </summary>
    private string FailureContext(string action, Guid scenarioId)
    {
        if (action == RemoveAction)
        {
            var scenario = Evaluation.Scenarios.FirstOrDefault(candidate =>
                candidate.Id == scenarioId
            );

            return scenario?.Name ?? Evaluation.Name;
        }

        var annualReturn = ScenarioForm.Format(Evaluation.CalculationSettings?.AnnualReturnPercent);

        return $"{text["ScenarioAnnualReturn"]}: {annualReturn ?? text["OfferOpen"]} %";
    }
}
