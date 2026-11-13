using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Steervo.Application.Evaluations;
using Steervo.Application.Scenarios;
using Steervo.Calculations;
using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations.Scenarios;

public sealed record ScenarioPanel(Evaluation Evaluation, Scenario Scenario, ScenarioResult Result);
public sealed class DetailsModel(ScenarioWorkspace workspace, ICurrentUser current, IStringLocalizer<Ui> text) : PageModel
{
    public Evaluation Evaluation { get; private set; } = null!;
    public Scenario Scenario { get; private set; } = null!;
    public ScenarioResult Result { get; private set; } = null!;
    public AccessLevel Access => Evaluation.AccessFor(current.Id)!.Value;
    public int Month { get; private set; }
    public string Period { get; private set; } = "month";
    public int PageNumber { get; private set; }
    public int PageCount { get; private set; }
    public IReadOnlyList<ScenarioMonth[]> Periods { get; private set; } = [];
    public IReadOnlyList<CostItem> Costs => Evaluation.Costs.Where(c => c.CandidateId is null || c.CandidateId == Evaluation.FindOffer(Scenario.OfferId).CandidateId).OrderBy(c => c.Name).ToArray();
    [BindProperty] public long Version { get; set; }
    [BindProperty] public ResidualForm Residual { get; set; } = new();
    [BindProperty] public AdjustmentForm Adjustment { get; set; } = new();
    [BindProperty] public string? CopyName { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? LeftId { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? RightId { get; set; }
    public bool EditResidual { get; private set; }
    public bool EditAdjustment { get; private set; }
    public string? FailedAction { get; private set; }
    public string ContextUrl => $"/evaluations/{Evaluation.Id}/scenarios/{Scenario.Id}?month={Month}&period={Period}&pageNumber={PageNumber}&leftId={LeftId}&rightId={RightId}";
    public string? Notice { get; private set; }
    public async Task<IActionResult> OnGetAsync(Guid id, Guid scenarioId, int? month, int? residualMonth, Guid? costId, string? period, int pageNumber = 1)
    {
        try
        {
            await LoadAsync(id, scenarioId, month, period, pageNumber); Version = Evaluation.Version;
            EditResidual = residualMonth.HasValue; EditAdjustment = costId.HasValue;
            var point = Scenario.Residuals.SingleOrDefault(r => r.Month == (residualMonth ?? Month));
            Residual = point is null ? new() { Month = (residualMonth ?? Month).ToString(System.Globalization.CultureInfo.InvariantCulture) } : ResidualForm.From(point);
            if (costId.HasValue && Scenario.CostAdjustments.SingleOrDefault(a => a.CostId == costId) is { } adjustment) Adjustment = AdjustmentForm.From(adjustment);
            return Page();
        }
        catch (RuleException error) { return StatusCode(error.Message == "Forbidden" ? 403 : 404); }
    }
    public async Task<IActionResult> OnPostAsync(Guid id, Guid scenarioId, int? month, string? period, string action, int residualMonth, Guid costId, int pageNumber = 1)
    {
        try
        {
            await LoadAsync(id, scenarioId, month, period, pageNumber); Evaluation.Require(current.Id, AccessLevel.Editor);
            if (!ModelState.IsValid) throw new RuleException("InvalidScenario");
            switch (action)
            {
                case "residual": await workspace.ResidualAsync(id, Version, scenarioId, Residual.ToInput()); break;
                case "remove-residual": await workspace.RemoveResidualAsync(id, Version, scenarioId, residualMonth); break;
                case "adjustment": await workspace.AdjustmentAsync(id, Version, scenarioId, Adjustment.ToInput()); break;
                case "remove-adjustment": await workspace.RemoveAdjustmentAsync(id, Version, scenarioId, costId); break;
                case "copy":
                    var copyId = await workspace.CopyAsync(id, Version, scenarioId, CopyName ?? "");
                    var copyUrl = Url.Page("Details", null, new { id, scenarioId = copyId, month, period }, null, null, "scenario-title")!;
                    if (Request.Headers.ContainsKey("HX-Request")) { Response.Headers["HX-Redirect"] = copyUrl; return new EmptyResult(); }
                    return Redirect(copyUrl);
                default: throw new RuleException("InvalidValue");
            }
            var url = ContextUrl + (action.Contains("adjustment") ? "#scenario-adjustments" : "#scenario-residuals");
            if (Request.Headers.ContainsKey("HX-Request")) { Response.Headers["HX-Redirect"] = url; return new EmptyResult(); }
            return Redirect(url);
        }
        catch (RuleException error)
        {
            if (error.Message is "Forbidden" or "NotFound") return StatusCode(error.Message == "Forbidden" ? 403 : 404);
            FailedAction = action; Notice = text[error.Message]; Response.StatusCode = error.Message == "Conflict" ? 409 : 422;
            if (Request.Headers.ContainsKey("HX-Request")) return new JsonResult(new { error = Notice, version = Evaluation.Version, reviewUrl = ContextUrl, context = Scenario.Name });
            return Page();
        }
    }
    private async Task LoadAsync(Guid id, Guid scenarioId, int? month, string? period, int pageNumber)
    {
        Evaluation = await workspace.ReadAsync(id); Scenario = Evaluation.FindScenario(scenarioId);
        LeftId = Evaluation.Scenarios.Any(s => s.Id == LeftId) ? LeftId : scenarioId;
        RightId = Evaluation.Scenarios.Any(s => s.Id == RightId) ? RightId : null;
        Month = month ?? Evaluation.CostPlanning!.Months; Result = ScenarioCalculation.Calculate(Evaluation, scenarioId, Month);
        Period = period == "year" ? "year" : "month";
        var periods = Period == "year" ? Result.Months.GroupBy(m => Evaluation.CostPlanning!.DateFor(m.Month).Year).Select(g => g.ToArray()).ToArray() : Result.Months.Select(m => new[] { m }).ToArray();
        PageCount = Math.Max(1, (periods.Length + 23) / 24); PageNumber = Math.Clamp(pageNumber, 1, PageCount); Periods = periods.Skip((PageNumber - 1) * 24).Take(24).ToArray();
    }
}
