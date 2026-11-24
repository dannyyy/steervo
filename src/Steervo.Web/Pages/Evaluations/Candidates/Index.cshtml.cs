using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Steervo.Application.Evaluations;
using Steervo.Core;
using Steervo.Web.Pages.Shared;

namespace Steervo.Web.Pages.Evaluations.Candidates;

/// <summary>The list of cars of an evaluation with their assessment state; cars are added and reordered here.</summary>
public sealed class IndexModel(
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

    private const string AddAction = "add-candidate";
    private const string MoveAction = "move-candidate";

    /// <summary>The value the move form posts for "one place up".</summary>
    public const int MoveUp = -1;

    /// <summary>The value the move form posts for "one place down".</summary>
    public const int MoveDown = 1;

    public Evaluation Evaluation { get; private set; } = null!;

    public AccessLevel Access => Evaluation.AccessFor(current.Id)!.Value;

    public bool CanEdit => Access >= AccessLevel.Editor;

    public string? Notice { get; private set; }

    public bool Failed { get; private set; }

    public string? FailedAction { get; private set; }

    public bool CreationFailed { get; private set; }

    [BindProperty]
    public CandidateForm Input { get; set; } = new();

    public IEnumerable<Candidate> OrderedCandidates =>
        Evaluation.Candidates.OrderBy(candidate => candidate.Position);

    /// <summary>The state of every vehicle requirement for one car, in the order of the requirements.</summary>
    public FulfillmentSummary SummaryFor(Candidate candidate)
    {
        var entries = VehicleRequirements
            .Of(Evaluation)
            .OrderBy(criterion => criterion.Position)
            .Select(criterion => EntryFor(candidate, criterion))
            .ToList();

        return new FulfillmentSummary(candidate.Id.ToString(), entries);
    }

    /// <summary>The names of the required requirements a car does not meet.</summary>
    public static IReadOnlyList<string> UnmetRequiredNames(FulfillmentSummary summary) =>
        summary
            .Entries.Where(entry =>
                entry.Criterion.Priority == Priority.Required && entry.Result == Fulfillment.Unmet
            )
            .Select(entry => entry.Criterion.Name)
            .ToList();

    /// <summary>True when the car already stands at the end of the list it would be moved towards.</summary>
    public bool IsAtEnd(Candidate candidate, int direction) =>
        direction == MoveUp
            ? candidate.Position == 0
            : candidate.Position == Evaluation.Candidates.Count - 1;

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        try
        {
            Evaluation = await workspace.ReadAsync(id);
            return Page();
        }
        catch (RuleException error)
        {
            return AccessFailure(error);
        }
    }

    public async Task<IActionResult> OnPostAsync(
        Guid id,
        long version,
        string action,
        Guid itemId,
        int direction
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
                case AddAction:
                    var candidateId = await workspace.AddAsync(id, version, Input.ToInput());
                    return RedirectToNewCandidate(id, candidateId);

                case MoveAction:
                    await workspace.MoveAsync(id, version, itemId, direction);
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
            CreationFailed = action == AddAction;
            FailedAction = action;
            Response.StatusCode = error.Message == ConflictError ? 409 : 422;
        }

        Evaluation = await workspace.ReadAsync(id);
        return Request.Headers.ContainsKey(HtmxRequestHeader)
            ? Partial("_CandidateList", this)
            : Page();
    }

    private StatusCodeResult AccessFailure(RuleException error) =>
        StatusCode(error.Message == ForbiddenError ? 403 : 404);

    // A partial update only leaves the page through its own redirect header; a plain form post
    // is redirected the usual way.
    private IActionResult RedirectToNewCandidate(Guid id, Guid candidateId)
    {
        if (Request.Headers.ContainsKey(HtmxRequestHeader))
        {
            Response.Headers["HX-Redirect"] = Url.Page("Details", new { id, candidateId });
            return new EmptyResult();
        }

        return RedirectToPage("Details", new { id, candidateId });
    }

    private FulfillmentEntry EntryFor(Candidate candidate, Criterion criterion)
    {
        var result = Evaluation.ResultFor(candidate.Id, criterion.Id);

        // Only a rating has a value worth showing next to its state.
        var value =
            result == Fulfillment.Rated
                ? new AssessmentRow(
                    Evaluation,
                    candidate,
                    criterion,
                    Access,
                    string.Empty
                ).ValueText
                : null;

        return new FulfillmentEntry(criterion, result, value);
    }
}
