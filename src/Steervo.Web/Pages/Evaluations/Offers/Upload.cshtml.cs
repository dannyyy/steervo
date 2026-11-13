using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Steervo.Application.Documents;
using Steervo.Application.Evaluations;
using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations.Offers;

[RequestSizeLimit(DocumentFile.MaximumBytes + 100_000)]
[RequestFormLimits(MultipartBodyLengthLimit = DocumentFile.MaximumBytes)]
public sealed class UploadModel(EvaluationWorkspace evaluations, DocumentWorkspace documents, ICurrentUser current, IStringLocalizer<Ui> text) : PageModel
{
    public Evaluation Evaluation { get; private set; } = null!;
    public Offer Offer { get; private set; } = null!;
    public OfferDocument? Document { get; private set; }
    public AccessLevel Access => Evaluation.AccessFor(current.Id)!.Value;
    [BindProperty(SupportsGet = true)] public Guid? First { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? Second { get; set; }
    public string ReturnUrl => $"/evaluations/{Evaluation.Id}/offers/{Offer.Id}?first={First}&second={Second}#documents";
    [BindProperty] public long Version { get; set; }
    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public string? Note { get; set; }
    public async Task<IActionResult> OnGetAsync(Guid id, Guid offerId, Guid? documentId)
    {
        try
        {
            await LoadAsync(id, offerId, documentId);
            if (Offer.Archived) return RedirectToPage("Details", new { id, offerId });
            Version = Evaluation.Version; Name = Document?.Name ?? ""; return Page();
        }
        catch (RuleException error) { return StatusCode(error.Message == "Forbidden" ? 403 : 404); }
    }
    public async Task<IActionResult> OnPostAsync(Guid id, Guid offerId, Guid? documentId, IFormFile? upload)
    {
        try
        {
            await LoadAsync(id, offerId, documentId);
            if (!ModelState.IsValid || upload is null || upload.Length == 0) throw new RuleException("InvalidDocument");
            if (upload.Length > DocumentFile.MaximumBytes) throw new RuleException("DocumentTooLarge");
            await using var input = upload.OpenReadStream();
            await documents.UploadAsync(id, Version, offerId, documentId, Name, input, upload.FileName, Note, HttpContext.RequestAborted);
            if (Request.Headers.ContainsKey("HX-Request"))
            {
                Response.Headers["HX-Redirect"] = Url.Page("Details", null, new { id, offerId, first = First, second = Second }, null, null, "documents");
                return new EmptyResult();
            }
            return RedirectToPage("Details", null, new { id, offerId, first = First, second = Second }, "documents");
        }
        catch (RuleException error)
        {
            if (error.Message is "Forbidden" or "NotFound") return StatusCode(error.Message == "Forbidden" ? 403 : 404);
            ModelState.AddModelError("", text[error.Message]);
            Response.StatusCode = error.Message == "Conflict" ? 409 : 422;
            if (Request.Headers.ContainsKey("HX-Request")) return new JsonResult(new { error = text[error.Message].Value, version = Evaluation.Version,
                reviewUrl = ReturnUrl, context = $"{Offer.Latest.Terms.Name} · {text["OfferVersion", Offer.Latest.Number]}" });
            ModelState.AddModelError("", text["DocumentReselect"]);
            return Page();
        }
    }
    private async Task LoadAsync(Guid id, Guid offerId, Guid? documentId)
    {
        Evaluation = await evaluations.ReadAsync(id); Evaluation.Require(current.Id, AccessLevel.Editor);
        Offer = Evaluation.FindOffer(offerId);
        First = Evaluation.Offers.Any(item => item.Id == First && item.CandidateId == Offer.CandidateId) ? First : offerId;
        Second = Evaluation.Offers.Any(item => item.Id == Second && item.Id != First && item.CandidateId == Offer.CandidateId) ? Second : null;
        if (documentId.HasValue)
        {
            Document = Evaluation.FindDocument(documentId.Value);
            if (Document.OfferId != offerId) throw new RuleException("NotFound");
        }
    }
}
