using System.Globalization;
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
    public CandidateForm Input { get; private set; } = new();
    public async Task<IActionResult> OnGetAsync(Guid id, Guid candidateId)
    {
        try { await LoadAsync(id, candidateId); return Page(); }
        catch (RuleException error) { return StatusCode(error.Message == "Forbidden" ? 403 : 404); }
    }
    public async Task<IActionResult> OnPostAsync(Guid id, Guid candidateId, long version, string action, Guid criterionId,
        CandidateForm input, CandidateStatus status, string? reason, bool? booleanValue, string? measurementValue, int? ratingValue,
        AssessmentSource source, string? notes)
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
                    await workspace.AssessAsync(id, version, candidateId, criterionId, new(booleanValue, ParseMeasurement(measurementValue), ratingValue, source, notes)); break;
                case "clear-assessment": await workspace.ClearAssessmentAsync(id, version, candidateId, criterionId); break;
                default: throw new RuleException("InvalidValue");
            }
            Notice = text["Saved"];
        }
        catch (RuleException error)
        {
            if (error.Message is "Forbidden" or "NotFound") return StatusCode(error.Message == "Forbidden" ? 403 : 404);
            Failed = true; FailedAction = action; Notice = text[error.Message]; Response.StatusCode = error.Message == "Conflict" ? 409 : 422;
        }
        await LoadAsync(id, candidateId);
        if (FailedAction == "edit-candidate") Input = input;
        return Request.Headers.ContainsKey("HX-Request") ? Partial("_CandidateDetails", this) : Page();
    }
    internal static decimal? ParseMeasurement(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)) throw new RuleException("InvalidValue");
        return parsed;
    }
    private async Task LoadAsync(Guid id, Guid candidateId)
    {
        Evaluation = await workspace.ReadAsync(id);
        Candidate = Evaluation.FindCandidate(candidateId);
        Input = CandidateForm.From(Candidate);
    }
}
