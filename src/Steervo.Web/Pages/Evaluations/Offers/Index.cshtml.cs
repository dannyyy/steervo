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
    public bool ShowArchived { get; private set; }
    public IEnumerable<Offer> Offers => Evaluation.Offers.Where(offer => (!CandidateId.HasValue || offer.CandidateId == CandidateId) && (ShowArchived || !offer.Archived)).OrderByDescending(offer => offer.Latest.CreatedAt);
    public async Task<IActionResult> OnGetAsync(Guid id, Guid? candidateId, bool showArchived = false)
    {
        try
        {
            Evaluation = await workspace.ReadAsync(id);
            if (candidateId.HasValue) _ = Evaluation.FindCandidate(candidateId.Value);
            CandidateId = candidateId; ShowArchived = showArchived;
            return Page();
        }
        catch (RuleException error) { return StatusCode(error.Message == "Forbidden" ? 403 : 404); }
    }
}
