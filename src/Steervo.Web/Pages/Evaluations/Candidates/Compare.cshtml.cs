using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Steervo.Application.Evaluations;
using Steervo.Core;
using Steervo.Web.Pages.Shared;

namespace Steervo.Web.Pages.Evaluations.Candidates;

/// <summary>Up to five cars side by side: technical data and the assessment of every vehicle requirement.</summary>
public sealed class CompareModel(
    CandidateWorkspace workspace,
    ICurrentUser current,
    IStringLocalizer<Ui> text
) : PageModel
{
    private const string ForbiddenError = "Forbidden";
    private const string NotFoundError = "NotFound";
    private const string ConflictError = "Conflict";
    private const string InvalidValueError = "InvalidValue";
    private const string SecondQueryKey = "second";

    public Evaluation Evaluation { get; private set; } = null!;

    public AccessLevel Access => Evaluation.AccessFor(current.Id)!.Value;

    public bool CanEdit => Access >= AccessLevel.Editor;

    public IReadOnlyList<Candidate> Selected { get; private set; } = [];

    public Candidate? First => Selected.ElementAtOrDefault(0);

    public Candidate? Second => Selected.ElementAtOrDefault(1);

    public IReadOnlyList<Guid> More => Selected.Skip(2).Select(candidate => candidate.Id).ToList();

    /// <summary>The cars that can still be added to the comparison, in list order.</summary>
    public IReadOnlyList<Candidate> Available =>
        Evaluation
            .Candidates.OrderBy(candidate => candidate.Position)
            .Where(candidate => !Selected.Contains(candidate))
            .ToList();

    /// <summary>True when the request named more cars than fit next to each other.</summary>
    public bool Limited { get; private set; }

    public string Query => ComparisonSelection.Query(Selected.Select(candidate => candidate.Id));

    public string QueryWithout(Candidate removed) =>
        ComparisonSelection.Query(
            Selected.Where(candidate => candidate != removed).Select(candidate => candidate.Id)
        );

    /// <summary>The address assessments are posted to; it keeps the current selection.</summary>
    public string PostUrl => $"/evaluations/{Evaluation.Id}/compare?{Query}";

    public bool HasSpecifications =>
        Selected.Any(candidate => candidate.Specifications.HasValues());

    public bool HasRequirements => VehicleCriteria.Any();

    public IEnumerable<Criterion> VehicleCriteria => VehicleRequirements.Of(Evaluation);

    public IReadOnlyList<VehicleRequirementGroup> RequirementGroups =>
        VehicleRequirements.Grouped(Evaluation);

    public string? Notice { get; private set; }

    public AssessmentDraft? Draft { get; private set; }

    public bool Failed { get; private set; }

    // An address that carries "second" at all, even empty, states the second car explicitly.
    private bool SecondCarIsStated => Request.Query.ContainsKey(SecondQueryKey);

    public async Task<IActionResult> OnGetAsync(Guid id, Guid? first, Guid? second, Guid[]? more)
    {
        try
        {
            await LoadAsync(id, first, second, more);
            return Page();
        }
        catch (RuleException error)
        {
            return AccessFailure(error);
        }
    }

    public async Task<IActionResult> OnPostAsync(
        Guid id,
        Guid? first,
        Guid? second,
        Guid[]? more,
        long version,
        string action,
        Guid candidateId,
        Guid criterionId,
        string? booleanValue,
        string? measurementValue,
        int? ratingValue,
        AssessmentSource source,
        string? notes
    )
    {
        try
        {
            await LoadAsync(id, first, second, more);
            Evaluation.Require(current.Id, AccessLevel.Editor);

            if (!ModelState.IsValid)
            {
                throw new RuleException(InvalidValueError);
            }

            switch (action)
            {
                case "assess-candidate":
                    var assessment = AssessmentForm.ToInput(
                        booleanValue,
                        measurementValue,
                        ratingValue,
                        source,
                        notes
                    );
                    await workspace.AssessAsync(id, version, candidateId, criterionId, assessment);
                    break;

                case "clear-assessment":
                    await workspace.ClearAssessmentAsync(id, version, candidateId, criterionId);
                    break;

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
            Notice = text[error.Message];

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
            Response.StatusCode = error.Message == ConflictError ? 409 : 422;
        }

        // Read again so the answer shows the stored state, including its new version.
        await LoadAsync(id, first, second, more);
        return Request.Headers.ContainsKey("HX-Request") ? Partial("_Comparison", this) : Page();
    }

    private StatusCodeResult AccessFailure(RuleException error) =>
        StatusCode(error.Message == ForbiddenError ? 403 : 404);

    private async Task LoadAsync(Guid id, Guid? first, Guid? second, Guid[]? more)
    {
        Evaluation = await workspace.ReadAsync(id);

        var ordered = Evaluation.Candidates.OrderBy(candidate => candidate.Position).ToList();
        var further = (more ?? [])
            .Where(item => Evaluation.Candidates.Any(candidate => candidate.Id == item))
            .ToList();

        var nothingStated =
            !first.HasValue && !second.HasValue && further.Count == 0 && !SecondCarIsStated;

        var chosen = nothingStated
            ? ordered.Take(ComparisonSelection.Maximum).ToList()
            : StatedCars(ordered, first, second, further);

        Limited = chosen.Count > ComparisonSelection.Maximum;
        Selected = chosen.Take(ComparisonSelection.Maximum).ToList();
    }

    private List<Candidate> StatedCars(
        List<Candidate> ordered,
        Guid? first,
        Guid? second,
        List<Guid> further
    )
    {
        List<Candidate> chosen = [];

        if (first.HasValue)
        {
            chosen.Add(Evaluation.FindCandidate(first.Value));
        }

        if (second.HasValue)
        {
            chosen.Add(Evaluation.FindCandidate(second.Value));
        }

        chosen.AddRange(further.Select(Evaluation.FindCandidate));
        chosen = chosen.Distinct().ToList();

        if (chosen.Count == 0 && ordered.Count > 0)
        {
            chosen.Add(ordered[0]);
        }

        // A link that names only the first car keeps opening it next to another one.
        var onlyFirstStated = chosen.Count == 1 && first.HasValue && !SecondCarIsStated;

        if (
            onlyFirstStated
            && ordered.FirstOrDefault(candidate => candidate != chosen[0]) is { } next
        )
        {
            chosen.Add(next);
        }

        return chosen;
    }
}
