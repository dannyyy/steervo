using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Steervo.Application.Evaluations;
using Steervo.Application.Offers;
using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations.Offers;

public sealed class CompareModel(OfferWorkspace workspace, ICurrentUser current, IStringLocalizer<Ui> text) : PageModel
{
    public Evaluation Evaluation { get; private set; } = null!;
    public AccessLevel Access => Evaluation.AccessFor(current.Id)!.Value;
    public Offer? First { get; private set; }
    public Offer? Second { get; private set; }
    public IReadOnlyList<Offer> Selected => new[] { First, Second }.OfType<Offer>().ToList();
    public string? Notice { get; private set; }
    public async Task<IActionResult> OnGetAsync(Guid id, Guid? first, Guid? second)
    {
        try
        {
            Evaluation = await workspace.ReadAsync(id);
            First = first.HasValue ? Evaluation.FindOffer(first.Value) : Evaluation.Offers.OrderBy(offer => offer.Archived).FirstOrDefault();
            Second = second.HasValue ? Evaluation.FindOffer(second.Value)
                : Request.Query.ContainsKey("second") ? null : Evaluation.Offers.OrderBy(offer => offer.Archived).FirstOrDefault(offer => offer.CandidateId == First?.CandidateId && offer.Id != First?.Id);
            if (Second is not null && (First?.Id == Second.Id || First?.CandidateId != Second.CandidateId)) { Second = null; Notice = text["OfferSameCandidateRequired"]; }
            return Page();
        }
        catch (RuleException error) { return StatusCode(error.Message == "Forbidden" ? 403 : 404); }
    }
}
