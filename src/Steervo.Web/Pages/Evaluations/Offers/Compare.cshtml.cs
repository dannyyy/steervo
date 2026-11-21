using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Steervo.Application.Evaluations;
using Steervo.Application.Offers;
using Steervo.Core;
using Steervo.Web.Pages.Evaluations.Candidates;

namespace Steervo.Web.Pages.Evaluations.Offers;

public sealed class CompareModel(OfferWorkspace workspace, ICurrentUser current) : PageModel
{
    public Evaluation Evaluation { get; private set; } = null!;
    public AccessLevel Access => Evaluation.AccessFor(current.Id)!.Value;
    public IReadOnlyList<Offer> Selected { get; private set; } = [];
    public Offer? First => Selected.ElementAtOrDefault(0);
    public Offer? Second => Selected.ElementAtOrDefault(1);
    public IReadOnlyList<Guid> More => Selected.Skip(2).Select(offer => offer.Id).ToList();
    public IReadOnlyList<Offer> Available { get; private set; } = [];
    public bool Limited { get; private set; }
    public string Query => ComparisonSelection.Query(Selected.Select(offer => offer.Id));
    public string QueryWithout(Offer removed) => ComparisonSelection.Query(Selected.Where(offer => offer != removed).Select(offer => offer.Id));
    public async Task<IActionResult> OnGetAsync(Guid id, Guid? first, Guid? second, Guid[]? more)
    {
        try
        {
            Evaluation = await workspace.ReadAsync(id);
            var ordered = Evaluation.Offers.OrderBy(offer => offer.Archived).ThenByDescending(offer => offer.Latest.CreatedAt).ToList();
            var further = (more ?? []).Where(item => Evaluation.Offers.Any(offer => offer.Id == item)).ToList();
            List<Offer> chosen;
            if (!first.HasValue && !second.HasValue && further.Count == 0 && !Request.Query.ContainsKey("second"))
                chosen = (ordered.Any(offer => !offer.Archived) ? ordered.Where(offer => !offer.Archived) : ordered).Take(ComparisonSelection.Maximum).ToList();
            else
            {
                chosen = [];
                if (first.HasValue) chosen.Add(Evaluation.FindOffer(first.Value));
                if (second.HasValue) chosen.Add(Evaluation.FindOffer(second.Value));
                chosen.AddRange(further.Select(Evaluation.FindOffer));
                chosen = chosen.Distinct().ToList();
                if (chosen.Count == 0 && ordered.Count > 0) chosen.Add(ordered[0]);
                // A link that names only the first offer keeps opening it next to another offer for the same car.
                if (chosen.Count == 1 && first.HasValue && !Request.Query.ContainsKey("second")
                    && ordered.FirstOrDefault(offer => offer != chosen[0] && offer.CandidateId == chosen[0].CandidateId) is { } next) chosen.Add(next);
            }
            Limited = chosen.Count > ComparisonSelection.Maximum;
            Selected = chosen.Take(ComparisonSelection.Maximum).ToList();
            Available = ordered.Where(offer => !Selected.Contains(offer)).ToList();
            return Page();
        }
        catch (RuleException error) { return StatusCode(error.Message == "Forbidden" ? 403 : 404); }
    }
}
