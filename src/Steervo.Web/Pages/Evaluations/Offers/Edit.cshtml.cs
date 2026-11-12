using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Steervo.Application.Evaluations;
using Steervo.Application.Offers;
using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations.Offers;

public sealed class EditModel(OfferWorkspace workspace, ICurrentUser current, IStringLocalizer<Ui> text) : PageModel
{
    public Evaluation Evaluation { get; private set; } = null!;
    public Offer? Offer { get; private set; }
    public AccessLevel Access => Evaluation.AccessFor(current.Id)!.Value;
    [BindProperty] public OfferForm Input { get; set; } = new();
    [BindProperty] public long Version { get; set; }
    public string? Notice { get; private set; }
    public async Task<IActionResult> OnGetAsync(Guid id, Guid? offerId, Guid? candidateId)
    {
        try
        {
            await LoadAsync(id, offerId);
            if (Offer?.Archived == true) return RedirectToPage("Details", new { id, offerId });
            Version = Evaluation.Version;
            if (Offer is not null) Input = OfferForm.From(Offer, Evaluation.Currency);
            else
            {
                Input.Currency = Evaluation.Currency;
                if (candidateId.HasValue) { var candidate = Evaluation.FindCandidate(candidateId.Value); Input.CandidateId = candidate.Id; Input.VehicleDescription = candidate.DisplayName; }
            }
            return Page();
        }
        catch (RuleException error) { return StatusCode(error.Message == "Forbidden" ? 403 : 404); }
    }
    public async Task<IActionResult> OnPostAsync(Guid id, Guid? offerId)
    {
        try
        {
            await LoadAsync(id, offerId);
            if (!ModelState.IsValid) throw new RuleException("InvalidOfferTerms");
            var terms = Input.ToTerms(); var lines = Input.Lines.Select(line => line.ToInput()).ToArray();
            if (Offer is null)
                offerId = await workspace.AddAsync(id, Version, Input.CandidateId, terms, lines, Input.Currency, Input.ChangeNote);
            else await workspace.ReviseAsync(id, Version, Offer.Id, terms, lines, Input.ChangeNote);
            return RedirectToPage("Details", new { id, offerId });
        }
        catch (RuleException error)
        {
            if (error.Message is "Forbidden" or "NotFound") return StatusCode(error.Message == "Forbidden" ? 403 : 404);
            Notice = text[error.Message]; Response.StatusCode = error.Message == "Conflict" ? 409 : 422;
            return Page();
        }
    }
    private async Task LoadAsync(Guid id, Guid? offerId)
    {
        Evaluation = await workspace.ReadAsync(id); Evaluation.Require(current.Id, AccessLevel.Editor);
        Offer = offerId.HasValue ? Evaluation.FindOffer(offerId.Value) : null;
    }
}
