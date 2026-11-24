using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Steervo.Application.Evaluations;
using Steervo.Core;
using Steervo.Web.Pages.Shared;

namespace Steervo.Web.Pages.Evaluations.Candidates;

/// <summary>The record of one car: technical data, the assessment of its requirements, notes and classification.</summary>
public sealed class DetailsModel(
    CandidateWorkspace workspace,
    ICurrentUser current,
    IStringLocalizer<Ui> text
) : PageModel
{
    private const string ForbiddenError = "Forbidden";
    private const string NotFoundError = "NotFound";
    private const string ConflictError = "Conflict";
    private const string InvalidValueError = "InvalidValue";
    private const string HtmxRequestHeader = "HX-Request";
    private const string SecondQueryKey = "second";

    private const string EditAction = "edit-candidate";
    private const string ClassifyAction = "classify-candidate";
    private const string AssessAction = "assess-candidate";
    private const string ClearAssessmentAction = "clear-assessment";
    private const string CopyAction = "copy-candidate";
    private const string RemoveAction = "remove-candidate";

    public Evaluation Evaluation { get; private set; } = null!;

    public Candidate Candidate { get; private set; } = null!;

    public AccessLevel Access => Evaluation.AccessFor(current.Id)!.Value;

    public bool CanEdit => Access >= AccessLevel.Editor;

    public string? Notice { get; private set; }

    public bool Failed { get; private set; }

    public string? FailedAction { get; private set; }

    public bool EditFailed => FailedAction == EditAction;

    public bool ClassifyFailed => FailedAction == ClassifyAction;

    public bool CopyFailed => FailedAction == CopyAction;

    public bool RemoveFailed => Failed && FailedAction == RemoveAction;

    public AssessmentDraft? Draft { get; private set; }

    public string? CopyName { get; private set; }

    public Guid? ComparisonFirst { get; private set; }

    public Guid? ComparisonSecond { get; private set; }

    public IReadOnlyList<Guid> ComparisonMore { get; private set; } = [];

    public string ComparisonQuery =>
        $"first={ComparisonFirst}&second={ComparisonSecond}{ComparisonSelection.More(ComparisonMore)}";

    /// <summary>The address every form of the record posts to; it keeps the comparison the car was opened from.</summary>
    public string PostUrl =>
        $"/evaluations/{Evaluation.Id}/candidates/{Candidate.Id}?{ComparisonQuery}";

    public string ComparisonUrl => $"/evaluations/{Evaluation.Id}/compare?{ComparisonQuery}";

    public IEnumerable<Candidate> OrderedCandidates =>
        Evaluation.Candidates.OrderBy(candidate => candidate.Position);

    public IReadOnlyList<Criterion> VehicleCriteria => VehicleRequirements.Of(Evaluation).ToList();

    public IReadOnlyList<VehicleRequirementGroup> RequirementGroups =>
        VehicleRequirements.Grouped(Evaluation);

    /// <summary>Required requirements the car does not meet.</summary>
    public IReadOnlyList<Criterion> UnmetRequired => RequiredWithResult(Fulfillment.Unmet);

    /// <summary>Required requirements the car meets only with an option.</summary>
    public IReadOnlyList<Criterion> OptionalRequired => RequiredWithResult(Fulfillment.Optional);

    public int OpenAssessmentCount =>
        VehicleCriteria.Count(criterion => ResultFor(criterion) == Fulfillment.Unknown);

    /// <summary>
    /// Technical data opens first; a returned assessment draft keeps its requirements in view.
    /// </summary>
    public bool RequirementsFirst => Draft is not null;

    /// <summary>The values of a rejected submission, so the page can put them back into its form.</summary>
    public string SubmittedFormJson =>
        JsonSerializer.Serialize(
            Request.Form.ToDictionary(entry => entry.Key, entry => entry.Value.ToString())
        );

    public string SubmittedCopyJson => JsonSerializer.Serialize(new { copyName = CopyName });

    /// <summary>
    /// The first and second car of the ribbon link to <paramref name="candidate"/>: a car of the
    /// current pair keeps the pair, any other car takes the first place next to the previous first.
    /// </summary>
    public (Guid? First, Guid? Second) ComparisonWith(Candidate candidate)
    {
        var isInPair = candidate.Id == ComparisonFirst || candidate.Id == ComparisonSecond;

        return isInPair ? (ComparisonFirst, ComparisonSecond) : (candidate.Id, ComparisonFirst);
    }

    public async Task<IActionResult> OnGetAsync(
        Guid id,
        Guid candidateId,
        Guid? first,
        Guid? second,
        Guid[]? more
    )
    {
        try
        {
            await LoadAsync(id, candidateId, first, second, more);
            return Page();
        }
        catch (RuleException error)
        {
            return AccessFailure(error);
        }
    }

    public async Task<IActionResult> OnPostAsync(
        Guid id,
        Guid candidateId,
        long version,
        string action,
        Guid criterionId,
        CandidateForm input,
        CandidateStatus status,
        string? reason,
        string? booleanValue,
        string? measurementValue,
        int? ratingValue,
        AssessmentSource source,
        string? notes,
        Guid? first,
        Guid? second,
        Guid[]? more,
        string? copyName
    )
    {
        try
        {
            (await workspace.ReadAsync(id)).Require(current.Id, AccessLevel.Editor);

            if (!ModelState.IsValid)
            {
                throw new RuleException(InvalidValueError);
            }

            switch (action)
            {
                case EditAction:
                    await workspace.EditAsync(id, version, candidateId, input.ToInput());
                    break;

                case ClassifyAction:
                    await workspace.ClassifyAsync(id, version, candidateId, status, reason);
                    break;

                case AssessAction:
                    var assessment = AssessmentForm.ToInput(
                        booleanValue,
                        measurementValue,
                        ratingValue,
                        source,
                        notes
                    );
                    await workspace.AssessAsync(id, version, candidateId, criterionId, assessment);
                    break;

                case ClearAssessmentAction:
                    await workspace.ClearAssessmentAsync(id, version, candidateId, criterionId);
                    break;

                case CopyAction:
                    var copyId = await workspace.CopyAsync(
                        id,
                        version,
                        candidateId,
                        copyName ?? ""
                    );
                    return RedirectAfterPost("Details", new { id, candidateId = copyId });

                case RemoveAction:
                    await workspace.RemoveAsync(id, version, candidateId);
                    return RedirectAfterPost("Index", new { id });

                default:
                    throw new RuleException(InvalidValueError);
            }

            Notice = text["Saved"];
        }
        catch (RuleException error)
        {
            if (error.Message is ForbiddenError or NotFoundError)
            {
                return AccessFailure(error);
            }

            Failed = true;
            FailedAction = action;
            Notice = text[error.Message];
            CopyName = copyName;

            if (action is AssessAction or ClearAssessmentAction)
            {
                // The rejected input goes back into the editor exactly as it was typed.
                Draft = new(
                    candidateId,
                    criterionId,
                    Request.Form["booleanValue"],
                    measurementValue,
                    Request.Form["ratingValue"],
                    Request.Form["source"],
                    notes,
                    Notice
                );
            }

            Response.StatusCode = error.Message == ConflictError ? 409 : 422;
        }

        await LoadAsync(id, candidateId, first, second, more);
        return Request.Headers.ContainsKey(HtmxRequestHeader)
            ? Partial("_CandidateDetails", this)
            : Page();
    }

    private StatusCodeResult AccessFailure(RuleException error) =>
        StatusCode(error.Message == ForbiddenError ? 403 : 404);

    // A partial update only leaves the page through its own redirect header; a plain form post
    // is redirected the usual way.
    private IActionResult RedirectAfterPost(string pageName, object routeValues)
    {
        if (Request.Headers.ContainsKey(HtmxRequestHeader))
        {
            Response.Headers["HX-Redirect"] = Url.Page(pageName, routeValues);
            return new EmptyResult();
        }

        return RedirectToPage(pageName, routeValues);
    }

    private async Task LoadAsync(Guid id, Guid candidateId, Guid? first, Guid? second, Guid[]? more)
    {
        Evaluation = await workspace.ReadAsync(id);
        Candidate = Evaluation.FindCandidate(candidateId);

        var firstExists = Evaluation.Candidates.Any(candidate => candidate.Id == first);
        ComparisonFirst = firstExists ? first : candidateId;
        ComparisonSecond = ResolveComparisonSecond(second);
        ComparisonMore = ComparisonSelection.Further(
            Evaluation,
            ComparisonFirst,
            ComparisonSecond,
            more
        );
    }

    private Guid? ResolveComparisonSecond(Guid? second)
    {
        var secondIsUsable = Evaluation.Candidates.Any(candidate =>
            candidate.Id == second && candidate.Id != ComparisonFirst
        );

        if (secondIsUsable)
        {
            return second;
        }

        // An address that carries "second" at all, even empty, asks for no second car.
        if (Request.Query.ContainsKey(SecondQueryKey))
        {
            return null;
        }

        return Evaluation
            .Candidates.OrderBy(candidate => candidate.Position)
            .FirstOrDefault(candidate => candidate.Id != ComparisonFirst)
            ?.Id;
    }

    private IReadOnlyList<Criterion> RequiredWithResult(Fulfillment result) =>
        VehicleCriteria
            .Where(criterion =>
                criterion.Priority == Priority.Required && ResultFor(criterion) == result
            )
            .ToList();

    private Fulfillment ResultFor(Criterion criterion) =>
        Evaluation.ResultFor(Candidate.Id, criterion.Id);
}
