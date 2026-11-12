using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Steervo.Application.Evaluations;
using Steervo.Application.Scenarios;
using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations.Scenarios;

public sealed class IndexModel(ScenarioWorkspace workspace, ICurrentUser current, IStringLocalizer<Ui> text) : PageModel
{
    public Evaluation Evaluation { get; private set; } = null!;
    public AccessLevel Access => Evaluation.AccessFor(current.Id)!.Value;
    [BindProperty] public string? AnnualReturn { get; set; }
    [BindProperty] public long Version { get; set; }
    public string? Notice { get; private set; }
    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        try { Evaluation = await workspace.ReadAsync(id); Version = Evaluation.Version; AnnualReturn = ScenarioForm.Format(Evaluation.CalculationSettings?.AnnualReturnPercent); return Page(); }
        catch (RuleException error) { return StatusCode(error.Message == "Forbidden" ? 403 : 404); }
    }
    public async Task<IActionResult> OnPostAsync(Guid id, string action, Guid scenarioId)
    {
        try
        {
            Evaluation = await workspace.ReadAsync(id); Evaluation.Require(current.Id, AccessLevel.Editor);
            if (!ModelState.IsValid) throw new RuleException("InvalidScenario");
            if (action == "settings") await workspace.SettingsAsync(id, Version, ScenarioForm.Number(AnnualReturn) ?? throw new RuleException("InvalidReturn"));
            else if (action == "remove") await workspace.RemoveAsync(id, Version, scenarioId);
            else throw new RuleException("InvalidValue");
            return RedirectToPage("Index", new { id });
        }
        catch (RuleException error)
        {
            if (error.Message is "Forbidden" or "NotFound") return StatusCode(error.Message == "Forbidden" ? 403 : 404);
            Notice = text[error.Message]; Response.StatusCode = error.Message == "Conflict" ? 409 : 422; return Page();
        }
    }
}
