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
            return RedirectToPage("Details", null, new { id, offerId }, "documents");
        }
        catch (RuleException error)
        {
            if (error.Message is "Forbidden" or "NotFound") return StatusCode(error.Message == "Forbidden" ? 403 : 404);
            ModelState.AddModelError("", text[error.Message]);
            ModelState.AddModelError("", text["DocumentReselect"]);
            Response.StatusCode = error.Message == "Conflict" ? 409 : 422; return Page();
        }
    }
    private async Task LoadAsync(Guid id, Guid offerId, Guid? documentId)
    {
        Evaluation = await evaluations.ReadAsync(id); Evaluation.Require(current.Id, AccessLevel.Editor);
        Offer = Evaluation.FindOffer(offerId);
        if (documentId.HasValue)
        {
            Document = Evaluation.FindDocument(documentId.Value);
            if (Document.OfferId != offerId) throw new RuleException("NotFound");
        }
    }
}
