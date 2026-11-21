using Steervo.Web.Pages.Shared;
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
    public IReadOnlyList<Candidate> Selected { get; private set; } = [];
    public Candidate? First => Selected.ElementAtOrDefault(0);
    public Candidate? Second => Selected.ElementAtOrDefault(1);
    public IReadOnlyList<Guid> More => Selected.Skip(2).Select(candidate => candidate.Id).ToList();
    public IReadOnlyList<Candidate> Available => Evaluation.Candidates.OrderBy(candidate => candidate.Position).Where(candidate => !Selected.Contains(candidate)).ToList();
    public bool Limited { get; private set; }
    public string Query => ComparisonSelection.Query(Selected.Select(candidate => candidate.Id));
    public string QueryWithout(Candidate removed) => ComparisonSelection.Query(Selected.Where(candidate => candidate != removed).Select(candidate => candidate.Id));
    public string? Notice { get; private set; }
    public AssessmentDraft? Draft { get; private set; }
    public bool Failed { get; private set; }
    public async Task<IActionResult> OnGetAsync(Guid id, Guid? first, Guid? second, Guid[]? more)
    {
        try { await LoadAsync(id, first, second, more); return Page(); }
        catch (RuleException error) { return StatusCode(error.Message == "Forbidden" ? 403 : 404); }
    }
    public async Task<IActionResult> OnPostAsync(Guid id, Guid? first, Guid? second, Guid[]? more, long version, string action,
        Guid candidateId, Guid criterionId, string? booleanValue, string? measurementValue, int? ratingValue, AssessmentSource source, string? notes)
    {
        try
        {
            await LoadAsync(id, first, second, more);
            Evaluation.Require(current.Id, AccessLevel.Editor);
            if (!ModelState.IsValid) throw new RuleException("InvalidValue");
            switch (action)
            {
                case "assess-candidate": await workspace.AssessAsync(id, version, candidateId, criterionId, AssessmentForm.ToInput(booleanValue, measurementValue, ratingValue, source, notes)); break;
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
        await LoadAsync(id, first, second, more);
        return Request.Headers.ContainsKey("HX-Request") ? Partial("_Comparison", this) : Page();
    }
    private async Task LoadAsync(Guid id, Guid? first, Guid? second, Guid[]? more)
    {
        Evaluation = await workspace.ReadAsync(id);
        var ordered = Evaluation.Candidates.OrderBy(candidate => candidate.Position).ToList();
        var further = (more ?? []).Where(item => Evaluation.Candidates.Any(candidate => candidate.Id == item)).ToList();
        List<Candidate> chosen;
        if (!first.HasValue && !second.HasValue && further.Count == 0 && !Request.Query.ContainsKey("second"))
            chosen = ordered.Take(ComparisonSelection.Maximum).ToList();
        else
        {
            chosen = [];
            if (first.HasValue) chosen.Add(Evaluation.FindCandidate(first.Value));
            if (second.HasValue) chosen.Add(Evaluation.FindCandidate(second.Value));
            chosen.AddRange(further.Select(Evaluation.FindCandidate));
            chosen = chosen.Distinct().ToList();
            if (chosen.Count == 0 && ordered.Count > 0) chosen.Add(ordered[0]);
            // A link that names only the first car keeps opening it next to another one.
            if (chosen.Count == 1 && first.HasValue && !Request.Query.ContainsKey("second") && ordered.FirstOrDefault(candidate => candidate != chosen[0]) is { } next) chosen.Add(next);
        }
        Limited = chosen.Count > ComparisonSelection.Maximum;
        Selected = chosen.Take(ComparisonSelection.Maximum).ToList();
    }
}
