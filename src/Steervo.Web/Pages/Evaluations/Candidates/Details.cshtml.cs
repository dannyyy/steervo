using Steervo.Web.Pages.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Steervo.Application.Evaluations;
using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations.Candidates;

public sealed class DetailsModel(CandidateWorkspace workspace, ICurrentUser current, IStringLocalizer<Ui> text) : PageModel
{
    public Evaluation Evaluation { get; private set; } = null!;
    public Candidate Candidate { get; private set; } = null!;
    public AccessLevel Access => Evaluation.AccessFor(current.Id)!.Value;
    public string? Notice { get; private set; }
    public bool Failed { get; private set; }
    public string? FailedAction { get; private set; }
    public AssessmentDraft? Draft { get; private set; }
    public Guid? ComparisonFirst { get; private set; }
    public Guid? ComparisonSecond { get; private set; }
    public async Task<IActionResult> OnGetAsync(Guid id, Guid candidateId, Guid? first, Guid? second)
    {
        try { await LoadAsync(id, candidateId, first, second); return Page(); }
        catch (RuleException error) { return StatusCode(error.Message == "Forbidden" ? 403 : 404); }
    }
    public async Task<IActionResult> OnPostAsync(Guid id, Guid candidateId, long version, string action, Guid criterionId,
        CandidateForm input, CandidateStatus status, string? reason, string? booleanValue, string? measurementValue, int? ratingValue,
        AssessmentSource source, string? notes, Guid? first, Guid? second)
    {
        try
        {
            (await workspace.ReadAsync(id)).Require(current.Id, AccessLevel.Editor);
            if (!ModelState.IsValid) throw new RuleException("InvalidValue");
            switch (action)
            {
                case "edit-candidate": await workspace.EditAsync(id, version, candidateId, input.ToInput()); break;
                case "classify-candidate": await workspace.ClassifyAsync(id, version, candidateId, status, reason); break;
                case "assess-candidate":
                    await workspace.AssessAsync(id, version, candidateId, criterionId, AssessmentForm.ToInput(booleanValue, measurementValue, ratingValue, source, notes)); break;
                case "clear-assessment": await workspace.ClearAssessmentAsync(id, version, candidateId, criterionId); break;
                default: throw new RuleException("InvalidValue");
            }
            Notice = text["Saved"];
        }
        catch (RuleException error)
        {
            if (error.Message is "Forbidden" or "NotFound") return StatusCode(error.Message == "Forbidden" ? 403 : 404);
            Failed = true; FailedAction = action; Notice = text[error.Message];
            if (action is "assess-candidate" or "clear-assessment")
                Draft = new(candidateId, criterionId, Request.Form["booleanValue"], measurementValue,
                    Request.Form["ratingValue"], Request.Form["source"], notes, Notice);
            Response.StatusCode = error.Message == "Conflict" ? 409 : 422;
        }
        await LoadAsync(id, candidateId, first, second);
        return Request.Headers.ContainsKey("HX-Request") ? Partial("_CandidateDetails", this) : Page();
    }
    private async Task LoadAsync(Guid id, Guid candidateId, Guid? first, Guid? second)
    {
        Evaluation = await workspace.ReadAsync(id);
        Candidate = Evaluation.FindCandidate(candidateId);
        ComparisonFirst = Evaluation.Candidates.Any(x => x.Id == first) ? first : candidateId;
        ComparisonSecond = Evaluation.Candidates.Any(x => x.Id == second && x.Id != ComparisonFirst) ? second
            : Request.Query.ContainsKey("second") ? null : Evaluation.Candidates.OrderBy(x => x.Position).FirstOrDefault(x => x.Id != ComparisonFirst)?.Id;
    }
}
