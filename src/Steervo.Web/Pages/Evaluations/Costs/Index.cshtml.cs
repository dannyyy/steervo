using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Steervo.Application.Costs;
using Steervo.Application.Evaluations;
using Steervo.Calculations;
using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations.Costs;

public sealed record CostPeriod(int From, int Through, CostProjection Projection);
public sealed class IndexModel(CostWorkspace workspace, ICurrentUser current, IStringLocalizer<Ui> text) : PageModel
{
    public Evaluation Evaluation { get; private set; } = null!;
    public AccessLevel Access => Evaluation.AccessFor(current.Id)!.Value;
    public Guid? CandidateId { get; private set; }
    public string Period { get; private set; } = "month";
    public int PageNumber { get; private set; } = 1;
    public int PageCount { get; private set; }
    public CostProjection? Projection { get; private set; }
    public IReadOnlyList<CostPeriod> Periods { get; private set; } = [];
    public string? Notice { get; private set; }
    public string? FailedAction { get; private set; }
    public string ContextUrl => $"/evaluations/{Evaluation.Id}/costs?candidateId={CandidateId}&period={Period}&pageNumber={PageNumber}";
    [BindProperty] public CostPlanningForm Plan { get; set; } = new();
    [BindProperty] public long Version { get; set; }
    public async Task<IActionResult> OnGetAsync(Guid id, Guid? candidateId, string? period, int pageNumber = 1)
    {
        try
        {
            await LoadAsync(id, candidateId, period, pageNumber); Version = Evaluation.Version; Plan = CostPlanningForm.From(Evaluation.CostPlanning); return Page();
        }
        catch (RuleException error) { return StatusCode(error.Message == "Forbidden" ? 403 : 404); }
    }
    public async Task<IActionResult> OnPostAsync(Guid id, Guid? candidateId, string? period, string action, Guid costId, int pageNumber = 1)
    {
        try
        {
            await LoadAsync(id, candidateId, period, pageNumber); Evaluation.Require(current.Id, AccessLevel.Editor);
            if (!ModelState.IsValid) throw new RuleException("InvalidCostPlanning");
            switch (action)
            {
                case "plan": await workspace.PlanAsync(id, Version, Plan.ParseStart(), Plan.ParseMonths()); break;
                case "remove-cost": await workspace.RemoveAsync(id, Version, costId); break;
                default: throw new RuleException("InvalidValue");
            }
            var url = ContextUrl + (action == "remove-cost" ? "#cost-items" : "#cost-plan-title");
            if (Request.Headers.ContainsKey("HX-Request")) { Response.Headers["HX-Redirect"] = url; return new EmptyResult(); }
            return Redirect(url);
        }
        catch (RuleException error)
        {
            if (error.Message is "Forbidden" or "NotFound") return StatusCode(error.Message == "Forbidden" ? 403 : 404);
            FailedAction = action; Notice = text[error.Message]; Response.StatusCode = error.Message == "Conflict" ? 409 : 422;
            if (Request.Headers.ContainsKey("HX-Request")) return new JsonResult(new { error = Notice, version = Evaluation.Version, reviewUrl = ContextUrl,
                context = action == "remove-cost" ? Evaluation.Costs.FirstOrDefault(c => c.Id == costId)?.Name ?? Evaluation.Name : Evaluation.CostPlanning is { } planning ? $"{planning.Start:MM.yyyy} · {text["CostMonthCount", planning.Months]}" : Evaluation.Name });
            if (action != "plan") Plan = CostPlanningForm.From(Evaluation.CostPlanning);
            return Page();
        }
    }
    private async Task LoadAsync(Guid id, Guid? candidateId, string? period, int pageNumber)
    {
        Evaluation = await workspace.ReadAsync(id); CandidateId = candidateId; Period = period == "year" ? "year" : "month";
        if (candidateId.HasValue) _ = Evaluation.FindCandidate(candidateId.Value);
        if (Evaluation.CostPlanning is not { } planning) return;
        Projection = CostProjection.Calculate(Evaluation, candidateId);
        var ranges = Period == "year"
            ? Enumerable.Range(1, planning.Months).GroupBy(m => planning.DateFor(m).Year).Select(g => (From: g.Min(), Through: g.Max())).ToArray()
            : Enumerable.Range(1, planning.Months).Select(m => (From: m, Through: m)).ToArray();
        PageCount = (ranges.Length + 23) / 24; PageNumber = Math.Clamp(pageNumber, 1, PageCount);
        Periods = ranges.Skip((PageNumber - 1) * 24).Take(24).Select(r => new CostPeriod(r.From, r.Through, CostProjection.Calculate(Evaluation, candidateId, r.From, r.Through))).ToArray();
    }
}
