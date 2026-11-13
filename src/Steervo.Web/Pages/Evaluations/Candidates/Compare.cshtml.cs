using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Steervo.Application.Evaluations;
using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations.Candidates;

public sealed class CompareModel(CandidateWorkspace workspace, ICurrentUser current, IStringLocalizer<Ui> text) : PageModel
{
    public Evaluation Evaluation { get; private set; } = null!;
    public AccessLevel Access => Evaluation.AccessFor(current.Id)!.Value;
    public Candidate? First { get; private set; }
    public Candidate? Second { get; private set; }
    public IReadOnlyList<Candidate> Selected => new[] { First, Second }.OfType<Candidate>().ToList();
    public string? Notice { get; private set; }
    public AssessmentDraft? Draft { get; private set; }
    public bool Failed { get; private set; }
    public async Task<IActionResult> OnGetAsync(Guid id, Guid? first, Guid? second)
    {
        try { await LoadAsync(id, first, second); return Page(); }
        catch (RuleException error) { return StatusCode(error.Message == "Forbidden" ? 403 : 404); }
    }
    public async Task<IActionResult> OnPostAsync(Guid id, Guid? first, Guid? second, long version, string action,
        Guid candidateId, Guid criterionId, bool? booleanValue, string? measurementValue, int? ratingValue, AssessmentSource source, string? notes)
    {
        try
        {
            await LoadAsync(id, first, second);
            Evaluation.Require(current.Id, AccessLevel.Editor);
            if (!ModelState.IsValid) throw new RuleException("InvalidValue");
            switch (action)
            {
                case "assess-candidate": await workspace.AssessAsync(id, version, candidateId, criterionId, new(booleanValue, DetailsModel.ParseMeasurement(measurementValue), ratingValue, source, notes)); break;
                case "clear-assessment": await workspace.ClearAssessmentAsync(id, version, candidateId, criterionId); break;
                default: throw new RuleException("InvalidValue");
            }
            Notice = text["Saved"];
        }
        catch (RuleException error)
        {
            if (error.Message is "Forbidden" or "NotFound") return StatusCode(error.Message == "Forbidden" ? 403 : 404);
            Failed = true; Notice = text[error.Message];
            Draft = new(candidateId, criterionId, Request.Form["booleanValue"], measurementValue,
                Request.Form["ratingValue"], Request.Form["source"], notes, Notice);
            Response.StatusCode = error.Message == "Conflict" ? 409 : 422;
        }
        await LoadAsync(id, first, second);
        return Request.Headers.ContainsKey("HX-Request") ? Partial("_Comparison", this) : Page();
    }
    private async Task LoadAsync(Guid id, Guid? first, Guid? second)
    {
        Evaluation = await workspace.ReadAsync(id);
        First = first.HasValue ? Evaluation.FindCandidate(first.Value) : Evaluation.Candidates.OrderBy(x => x.Position).FirstOrDefault();
        Second = second.HasValue ? Evaluation.FindCandidate(second.Value)
            : Request.Query.ContainsKey("second") ? null : Evaluation.Candidates.OrderBy(x => x.Position).FirstOrDefault(x => x.Id != First?.Id);
        if (First is not null && First.Id == Second?.Id) { Second = null; Failed = true; Notice = text["SelectDifferentCandidates"]; }
    }
}
