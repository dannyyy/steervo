using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Steervo.Application.Evaluations;
using Steervo.Application.Offers;
using Steervo.Core;
using Steervo.Web.Pages.Shared;

namespace Steervo.Web.Pages.Evaluations.Offers;

/// <summary>Lists the offers of an evaluation, optionally only those for one car.</summary>
public sealed class IndexModel(OfferWorkspace workspace, ICurrentUser current) : PageModel
{
    private const int MoveUp = -1;
    private const int MoveDown = 1;

    private const string MoveOfferAction = "move-offer";
    private const string ForbiddenCode = "Forbidden";
    private const string NotFoundCode = "NotFound";

    public Evaluation Evaluation { get; private set; } = null!;

    public AccessLevel Access => Evaluation.AccessFor(current.Id)!.Value;

    public bool CanEdit => Access >= AccessLevel.Editor;

    public Guid? CandidateId { get; private set; }

    public IEnumerable<Offer> Offers =>
        Evaluation
            .Offers.Where(offer => !CandidateId.HasValue || offer.CandidateId == CandidateId)
            .OrderBy(offer => offer.Position);

    /// <summary>The directions an offer can be moved in the list, in the order of their buttons.</summary>
    public IReadOnlyList<int> MoveDirections => [MoveUp, MoveDown];

    /// <summary>The requirements that are answered per offer, in their configured order.</summary>
    public IReadOnlyList<Criterion> OfferCriteria =>
        Evaluation
            .Criteria.Where(criterion => criterion.Scope == CriterionScope.Offer)
            .OrderBy(criterion => criterion.Position)
            .ToList();

    /// <summary>How the offer answers each of the given requirements, for the summary on its card.</summary>
    public IReadOnlyList<FulfillmentEntry> FulfillmentEntries(
        Offer offer,
        IReadOnlyList<Criterion> criteria
    )
    {
        return criteria.Select(criterion => FulfillmentEntryFor(offer, criterion)).ToList();
    }

    /// <summary>The names of the mandatory requirements an offer does not meet.</summary>
    public IReadOnlyList<string> UnmetRequirements(IEnumerable<FulfillmentEntry> entries)
    {
        return entries
            .Where(entry =>
                entry.Criterion.Priority == Priority.Required && entry.Result == Fulfillment.Unmet
            )
            .Select(entry => entry.Criterion.Name)
            .ToList();
    }

    public bool MovesUp(int direction)
    {
        return direction == MoveUp;
    }

    public async Task<IActionResult> OnGetAsync(Guid id, Guid? candidateId)
    {
        try
        {
            Evaluation = await workspace.ReadAsync(id);

            // Looking the car up rejects a filter for a car that does not belong to this evaluation.
            if (candidateId.HasValue)
            {
                _ = Evaluation.FindCandidate(candidateId.Value);
            }

            CandidateId = candidateId;

            return Page();
        }
        catch (RuleException error)
        {
            return AccessFailure(error);
        }
    }

    public async Task<IActionResult> OnPostAsync(
        Guid id,
        Guid? candidateId,
        long version,
        string action,
        Guid itemId,
        int direction
    )
    {
        try
        {
            if (action != MoveOfferAction)
            {
                throw new RuleException("InvalidValue");
            }

            await workspace.MoveAsync(id, version, itemId, direction, candidateId);
        }
        catch (RuleException error)
        {
            if (error.Message is ForbiddenCode or NotFoundCode)
            {
                return AccessFailure(error);
            }

            // A stale or invalid move changes nothing; the reloaded list shows the current order.
        }

        return RedirectToPage("Index", new { id, candidateId });
    }

    private FulfillmentEntry FulfillmentEntryFor(Offer offer, Criterion criterion)
    {
        var row = new OfferAssessmentRow(Evaluation, offer, criterion, Access);
        var result = row.Result;
        var rating =
            result == Fulfillment.Rated && row.Assessment?.RatingValue is { } value
                ? $"{value} / 5"
                : null;

        return new FulfillmentEntry(criterion, result, rating);
    }

    private StatusCodeResult AccessFailure(RuleException error)
    {
        return StatusCode(
            error.Message == ForbiddenCode
                ? StatusCodes.Status403Forbidden
                : StatusCodes.Status404NotFound
        );
    }
}
