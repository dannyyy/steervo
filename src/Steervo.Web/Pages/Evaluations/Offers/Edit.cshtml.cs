using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Steervo.Application.Evaluations;
using Steervo.Application.Documents;
using Steervo.Application.Offers;
using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations.Offers;

[RequestSizeLimit(DocumentFile.MaximumBytes + 1_000_000)]
[RequestFormLimits(MultipartBodyLengthLimit = DocumentFile.MaximumBytes)]
public sealed class EditModel(OfferWorkspace workspace, ICurrentUser current, IStringLocalizer<Ui> text, IDocumentStorage documents) : PageModel
{
    public Evaluation Evaluation { get; private set; } = null!;
    public Offer? Offer { get; private set; }
    public AccessLevel Access => Evaluation.AccessFor(current.Id)!.Value;
    [BindProperty] public OfferForm Input { get; set; } = new();
    [BindProperty] public long Version { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? First { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? Second { get; set; }
    public string ReturnUrl => Offer is null ? $"/evaluations/{Evaluation.Id}/offers" : $"/evaluations/{Evaluation.Id}/offers/{Offer.Id}?first={First}&second={Second}";
    public string? Notice { get; private set; }
    public IReadOnlyList<Criterion> LineCriteria => Evaluation.Criteria.Where(criterion => criterion is { Scope: CriterionScope.Offer, Type: CriterionType.Boolean }).OrderBy(criterion => criterion.Position).ToList();
    public async Task<IActionResult> OnGetAsync(Guid id, Guid? offerId, Guid? candidateId)
    {
        try
        {
            await LoadAsync(id, offerId);
            if (Evaluation.Currency is null) return RedirectToPage("/Evaluations/Details", new { id, section = "settings" });
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
    public async Task<IActionResult> OnPostAsync(Guid id, Guid? offerId, IFormFile? upload)
    {
        try
        {
            await LoadAsync(id, offerId);
            if (!ModelState.IsValid) throw new RuleException("InvalidOfferTerms");
            var terms = Input.ToTerms(); var lines = Input.Lines.Select(line => line.ToInput()).ToArray();
            if (Evaluation.Version != Version) throw new RuleException("Conflict");
            if (Offer is null) _ = Evaluation.FindCandidate(Input.CandidateId);
            DocumentFile? attachment = null;
            if (upload is not null) {
                if (upload.Length == 0 || !upload.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) throw new RuleException("InvalidDocument");
                if (upload.Length > DocumentFile.MaximumBytes) throw new RuleException("DocumentTooLarge");
                await using (var probe = upload.OpenReadStream()) {
                    var header = new byte[5];
                    var count = await probe.ReadAtLeastAsync(header, header.Length, false, HttpContext.RequestAborted);
                    if (count != header.Length || !header.AsSpan().SequenceEqual("%PDF-"u8)) throw new RuleException("InvalidDocument");
                }
                await using var stream = upload.OpenReadStream();
                attachment = await documents.SaveAsync(stream, upload.FileName, HttpContext.RequestAborted);
            }
            if (Offer is null)
                offerId = await workspace.AddAsync(id, Version, Input.CandidateId, terms, lines, Input.Currency, Input.ChangeNote, attachment);
            else await workspace.ReviseAsync(id, Version, Offer.Id, terms, lines, Input.ChangeNote, attachment);
            if (Request.Headers.ContainsKey("HX-Request"))
            {
                Response.Headers["HX-Redirect"] = Url.Page("Details", new { id, offerId, first = First, second = Second });
                return new EmptyResult();
            }
            return RedirectToPage("Details", new { id, offerId, first = First, second = Second });
        }
        catch (RuleException error)
        {
            if (error.Message is "Forbidden" or "NotFound") return StatusCode(error.Message == "Forbidden" ? 403 : 404);
            Notice = text[error.Message];
            if (!Request.Headers.ContainsKey("HX-Request") && upload is not null) Notice += " " + text["DocumentReselect"];
            Response.StatusCode = error.Message == "Conflict" ? 409 : 422;
            if (Request.Headers.ContainsKey("HX-Request")) return new JsonResult(new { error = Notice, version = Evaluation.Version,
                reviewUrl = ReturnUrl, context = Offer is null ? Evaluation.Name : $"{Offer.Latest.Terms.Name} · {text["OfferVersion", Offer.Latest.Number]}" });
            return Page();
        }
    }
    private async Task LoadAsync(Guid id, Guid? offerId)
    {
        Evaluation = await workspace.ReadAsync(id); Evaluation.Require(current.Id, AccessLevel.Editor);
        Offer = offerId.HasValue ? Evaluation.FindOffer(offerId.Value) : null;
        First = Evaluation.Offers.Any(item => item.Id == First && item.CandidateId == Offer?.CandidateId) ? First : Offer?.Id;
        Second = Evaluation.Offers.Any(item => item.Id == Second && item.Id != First && item.CandidateId == Offer?.CandidateId) ? Second : null;
    }
}
