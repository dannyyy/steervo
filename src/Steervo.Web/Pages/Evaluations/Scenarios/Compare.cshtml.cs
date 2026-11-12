using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Steervo.Application.Evaluations;
using Steervo.Application.Scenarios;
using Steervo.Calculations;
using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations.Scenarios;

public sealed class CompareModel(ScenarioWorkspace workspace, ICurrentUser current) : PageModel
{
    public Evaluation Evaluation { get; private set; } = null!;
    public AccessLevel Access => Evaluation.AccessFor(current.Id)!.Value;
    public Guid? LeftId { get; private set; }
    public Guid? RightId { get; private set; }
    public int Month { get; private set; }
    public ScenarioPanel? Left { get; private set; }
    public ScenarioPanel? Right { get; private set; }
    public async Task<IActionResult> OnGetAsync(Guid id, Guid? leftId, Guid? rightId, int? month)
    {
        try
        {
            Evaluation = await workspace.ReadAsync(id); LeftId = leftId; RightId = rightId;
            if (Evaluation.CostPlanning is null) return RedirectToPage("Index", new { id });
            Month = month ?? Evaluation.CostPlanning.Months;
            if (Month < 1 || Month > Evaluation.CostPlanning.Months) throw new RuleException("NotFound");
            if (leftId.HasValue) Left = new(Evaluation, Evaluation.FindScenario(leftId.Value), ScenarioCalculation.Calculate(Evaluation, leftId.Value, Month));
            if (rightId.HasValue) Right = new(Evaluation, Evaluation.FindScenario(rightId.Value), ScenarioCalculation.Calculate(Evaluation, rightId.Value, Month));
            return Page();
        }
        catch (RuleException error) { return StatusCode(error.Message == "Forbidden" ? 403 : 404); }
    }
}
