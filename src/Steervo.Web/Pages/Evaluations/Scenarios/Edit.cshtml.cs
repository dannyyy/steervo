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
            return RedirectToPage("Details", new { id, scenarioId });
        }
        catch (RuleException error)
        {
            if (error.Message is "Forbidden" or "NotFound") return StatusCode(error.Message == "Forbidden" ? 403 : 404);
            Notice = text[error.Message]; Response.StatusCode = error.Message == "Conflict" ? 409 : 422; return Page();
        }
    }
    private async Task LoadAsync(Guid id, Guid? scenarioId)
    {
        Evaluation = await workspace.ReadAsync(id); Evaluation.Require(current.Id, AccessLevel.Editor);
        Scenario = scenarioId.HasValue ? Evaluation.FindScenario(scenarioId.Value) : null;
    }
}
