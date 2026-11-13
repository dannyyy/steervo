using Steervo.Web.Pages.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Steervo.Application.Evaluations;
using Steervo.Application.Offers;
using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations.Offers;

public sealed class DetailsModel(OfferWorkspace workspace, ICurrentUser current, IStringLocalizer<Ui> text) : PageModel
{
    public Evaluation Evaluation { get; private set; } = null!;
    public Offer Offer { get; private set; } = null!;
    public OfferRevision Revision { get; private set; } = null!;
    public bool Current => Revision.Number == Offer.Latest.Number;
    public AccessLevel Access => Evaluation.AccessFor(current.Id)!.Value;
    public string? Notice { get; private set; }
    public bool Failed { get; private set; }
    public AssessmentDraft? Draft { get; private set; }
    [BindProperty(SupportsGet = true)] public Guid? First { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? Second { get; set; }
    public string ReturnContext => $"?first={First}&second={Second}";
    public async Task<IActionResult> OnGetAsync(Guid id, Guid offerId, int? revision)
    {
        try { await LoadAsync(id, offerId, revision); return Page(); }
        catch (RuleException error) { return StatusCode(error.Message == "Forbidden" ? 403 : 404); }
    }
    public async Task<IActionResult> OnPostAsync(Guid id, Guid offerId, int? revision, long version, string action,
        Guid criterionId, string? booleanValue, string? measurementValue, int? ratingValue, AssessmentSource source, string? notes, Guid documentId)
    {
        try
        {
            await LoadAsync(id, offerId, revision); Evaluation.Require(current.Id, AccessLevel.Editor);
            if (!ModelState.IsValid || !Current) throw new RuleException("InvalidValue");
            switch (action)
            {
                case "archive-offer": await workspace.ArchiveAsync(id, version, offerId, true); break;
                case "restore-offer": await workspace.ArchiveAsync(id, version, offerId, false); break;
                case "assess-offer": await workspace.AssessAsync(id, version, offerId, criterionId, AssessmentForm.ToInput(booleanValue, measurementValue, ratingValue, source, notes)); break;
                case "clear-assessment": await workspace.ClearAssessmentAsync(id, version, offerId, criterionId); break;
                case "remove-document": await workspace.RemoveDocumentAsync(id, version, offerId, documentId); break;
                default: throw new RuleException("InvalidValue");
            }
            Notice = text["Saved"];
        }
        catch (RuleException error)
        {
            if (error.Message is "Forbidden" or "NotFound") return StatusCode(error.Message == "Forbidden" ? 403 : 404);
            Failed = true; Notice = text[error.Message];
            if (action is "assess-offer" or "clear-assessment")
                Draft = new(offerId, criterionId, Request.Form["booleanValue"], measurementValue, Request.Form["ratingValue"], Request.Form["source"], notes, Notice);
            Response.StatusCode = error.Message == "Conflict" ? 409 : 422;
        }
        await LoadAsync(id, offerId, revision);
        return Request.Headers.ContainsKey("HX-Request") ? Partial("_OfferDetails", this) : Page();
    }
    private async Task LoadAsync(Guid id, Guid offerId, int? revision)
    {
        Evaluation = await workspace.ReadAsync(id); Offer = Evaluation.FindOffer(offerId);
        Revision = revision.HasValue ? Offer.Revision(revision.Value) : Offer.Latest;
        First = Evaluation.Offers.Any(item => item.Id == First && item.CandidateId == Offer.CandidateId) ? First : offerId;
        Second = Evaluation.Offers.Any(item => item.Id == Second && item.Id != First && item.CandidateId == Offer.CandidateId) ? Second
            : Request.Query.ContainsKey("second") ? null : Evaluation.Offers.OrderBy(item => item.Archived).FirstOrDefault(item => item.CandidateId == Offer.CandidateId && item.Id != First)?.Id;
    }
}
