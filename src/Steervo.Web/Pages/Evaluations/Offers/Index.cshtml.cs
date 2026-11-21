using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Steervo.Application.Evaluations;
using Steervo.Application.Offers;
using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations.Offers;

public sealed class IndexModel(OfferWorkspace workspace, ICurrentUser current) : PageModel
{
    public Evaluation Evaluation { get; private set; } = null!;
    public AccessLevel Access => Evaluation.AccessFor(current.Id)!.Value;
    public Guid? CandidateId { get; private set; }
    public IEnumerable<Offer> Offers => Evaluation.Offers.Where(offer => !CandidateId.HasValue || offer.CandidateId == CandidateId).OrderBy(offer => offer.Position);
    public async Task<IActionResult> OnGetAsync(Guid id, Guid? candidateId)
    {
        try
        {
            Evaluation = await workspace.ReadAsync(id);
            if (candidateId.HasValue) _ = Evaluation.FindCandidate(candidateId.Value);
            CandidateId = candidateId;
            return Page();
        }
        catch (RuleException error) { return StatusCode(error.Message == "Forbidden" ? 403 : 404); }
    }
    public async Task<IActionResult> OnPostAsync(Guid id, Guid? candidateId, long version, string action, Guid itemId, int direction)
    {
        try
        {
            if (action != "move-offer") throw new RuleException("InvalidValue");
            await workspace.MoveAsync(id, version, itemId, direction, candidateId);
        }
        catch (RuleException error)
        {
            if (error.Message is "Forbidden" or "NotFound") return StatusCode(error.Message == "Forbidden" ? 403 : 404);
            // A stale or invalid move changes nothing; the reloaded list shows the current order.
        }
        return RedirectToPage("Index", new { id, candidateId });
    }
}
