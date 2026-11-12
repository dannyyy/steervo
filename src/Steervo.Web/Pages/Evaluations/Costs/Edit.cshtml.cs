using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Steervo.Application.Costs;
using Steervo.Application.Evaluations;
using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations.Costs;

public sealed class EditModel(CostWorkspace workspace, ICurrentUser current, IStringLocalizer<Ui> text) : PageModel
{
    public Evaluation Evaluation { get; private set; } = null!;
    public CostItem? Cost { get; private set; }
    [BindProperty] public CostForm Input { get; set; } = new();
    [BindProperty] public long Version { get; set; }
    public string? Notice { get; private set; }
    public async Task<IActionResult> OnGetAsync(Guid id, Guid? costId, Guid? candidateId)
    {
        try
        {
            await LoadAsync(id, costId);
            if (Evaluation.CostPlanning is null || Evaluation.Currency is null) return RedirectToPage("Index", new { id });
            Version = Evaluation.Version;
            if (Cost is not null) Input = CostForm.From(Cost);
            else if (candidateId.HasValue) { _ = Evaluation.FindCandidate(candidateId.Value); Input.CandidateId = candidateId; }
            return Page();
        }
        catch (RuleException error) { return StatusCode(error.Message == "Forbidden" ? 403 : 404); }
    }
    public async Task<IActionResult> OnPostAsync(Guid id, Guid? costId)
    {
        try
        {
            await LoadAsync(id, costId);
            if (!ModelState.IsValid) throw new RuleException("InvalidCost");
            if (Cost is null) costId = await workspace.AddAsync(id, Version, Input.ToInput());
            else await workspace.EditAsync(id, Version, Cost.Id, Input.ToInput());
            return RedirectToPage("Index", null, new { id, candidateId = Input.CandidateId }, "cost-" + costId);
        }
        catch (RuleException error)
        {
            if (error.Message is "Forbidden" or "NotFound") return StatusCode(error.Message == "Forbidden" ? 403 : 404);
            Notice = text[error.Message]; Response.StatusCode = error.Message == "Conflict" ? 409 : 422; return Page();
        }
    }
    private async Task LoadAsync(Guid id, Guid? costId)
    {
        Evaluation = await workspace.ReadAsync(id); Evaluation.Require(current.Id, AccessLevel.Editor);
        Cost = costId.HasValue ? Evaluation.FindCost(costId.Value) : null;
    }
}
