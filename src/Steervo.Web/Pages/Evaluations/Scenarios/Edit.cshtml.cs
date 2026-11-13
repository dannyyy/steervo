using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Steervo.Application.Evaluations;
using Steervo.Application.Scenarios;
using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations.Scenarios;

public sealed class EditModel(ScenarioWorkspace workspace, ICurrentUser current, IStringLocalizer<Ui> text) : PageModel
{
    public Evaluation Evaluation { get; private set; } = null!;
    public AccessLevel Access => Evaluation.AccessFor(current.Id)!.Value;
    [BindProperty(SupportsGet = true)] public Guid? LeftId { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? RightId { get; set; }
    [BindProperty(SupportsGet = true)] public int? Month { get; set; }
    [BindProperty(SupportsGet = true)] public string? Period { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    public string ReturnUrl => Scenario is null ? $"/evaluations/{Evaluation.Id}/scenarios" : $"/evaluations/{Evaluation.Id}/scenarios/{Scenario.Id}?month={Month}&period={Period}&pageNumber={PageNumber}&leftId={LeftId}&rightId={RightId}#scenario-title";
    public Scenario? Scenario { get; private set; }
    public Offer? Offer { get; private set; }
    [BindProperty] public ScenarioForm Input { get; set; } = new();
    [BindProperty] public long Version { get; set; }
    public string? Notice { get; private set; }
    public async Task<IActionResult> OnGetAsync(Guid id, Guid? scenarioId, Guid? offerId)
    {
        try
        {
            await LoadAsync(id, scenarioId);
            if (Evaluation.CostPlanning is null || Evaluation.Currency is null) return RedirectToPage("Index", new { id });
            Version = Evaluation.Version;
            if (Scenario is not null) { Offer = Evaluation.FindOffer(Scenario.OfferId); Input = ScenarioForm.From(Scenario); }
            else if (offerId.HasValue) { Offer = Evaluation.FindOffer(offerId.Value); Input = ScenarioForm.FromOffer(Offer); }
            return Page();
        }
        catch (RuleException error) { return StatusCode(error.Message == "Forbidden" ? 403 : 404); }
    }
    public async Task<IActionResult> OnPostAsync(Guid id, Guid? scenarioId)
    {
        try
        {
            await LoadAsync(id, scenarioId); Offer = Evaluation.FindOffer(Input.OfferId);
            if (!ModelState.IsValid) throw new RuleException("InvalidScenario");
            if (Scenario is null) scenarioId = await workspace.AddAsync(id, Version, Input.ToInput());
            else await workspace.EditAsync(id, Version, Scenario.Id, Input.ToInput());
            var url = Url.Page("Details", null, new { id, scenarioId, month = Month, period = Period, pageNumber = PageNumber, leftId = LeftId, rightId = RightId }, null, null, "scenario-title")!;
            if (Request.Headers.ContainsKey("HX-Request")) { Response.Headers["HX-Redirect"] = url; return new EmptyResult(); }
            return Redirect(url);
        }
        catch (RuleException error)
        {
            if (error.Message is "Forbidden" or "NotFound") return StatusCode(error.Message == "Forbidden" ? 403 : 404);
            Notice = text[error.Message]; Response.StatusCode = error.Message == "Conflict" ? 409 : 422;
            if (Request.Headers.ContainsKey("HX-Request")) return new JsonResult(new { error = Notice, version = Evaluation.Version, reviewUrl = ReturnUrl, context = Scenario?.Name ?? Evaluation.Name });
            return Page();
        }
    }
    private async Task LoadAsync(Guid id, Guid? scenarioId)
    {
        Evaluation = await workspace.ReadAsync(id); Evaluation.Require(current.Id, AccessLevel.Editor);
        LeftId = Evaluation.Scenarios.Any(s => s.Id == LeftId) ? LeftId : scenarioId;
        RightId = Evaluation.Scenarios.Any(s => s.Id == RightId) ? RightId : null;
        Period = Period == "year" ? "year" : "month"; PageNumber = Math.Max(1, PageNumber);
        if (Evaluation.CostPlanning is { } plan) Month = Math.Clamp(Month ?? plan.Months, 1, plan.Months);
        Scenario = scenarioId.HasValue ? Evaluation.FindScenario(scenarioId.Value) : null;
    }
}
