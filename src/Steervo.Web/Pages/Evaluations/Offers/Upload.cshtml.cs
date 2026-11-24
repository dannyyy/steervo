using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Steervo.Application.Documents;
using Steervo.Application.Evaluations;
using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations.Offers;

/// <summary>Adds a document to an offer, or uploads a new version of an existing document.</summary>
[RequestSizeLimit(DocumentFile.MaximumBytes + 100_000)]
[RequestFormLimits(MultipartBodyLengthLimit = DocumentFile.MaximumBytes)]
public sealed class UploadModel(
    EvaluationWorkspace evaluations,
    DocumentWorkspace documents,
    ICurrentUser current,
    IStringLocalizer<Ui> text
) : PageModel
{
    private const string DocumentsFragment = "documents";

    private const string ForbiddenCode = "Forbidden";
    private const string NotFoundCode = "NotFound";
    private const string ConflictCode = "Conflict";

    public Evaluation Evaluation { get; private set; } = null!;

    public Offer Offer { get; private set; } = null!;

    /// <summary>The document that receives a new version; null while a new document is added.</summary>
    public OfferDocument? Document { get; private set; }

    public AccessLevel Access => Evaluation.AccessFor(current.Id)!.Value;

    [BindProperty(SupportsGet = true)]
    public Guid? First { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? Second { get; set; }

    public string ReturnUrl =>
        $"/evaluations/{Evaluation.Id}/offers/{Offer.Id}?first={First}&second={Second}#documents";

    [BindProperty]
    public long Version { get; set; }

    [BindProperty]
    public string Name { get; set; } = "";

    [BindProperty]
    public string? Note { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id, Guid offerId, Guid? documentId)
    {
        try
        {
            await LoadAsync(id, offerId, documentId);

            Version = Evaluation.Version;
            Name = Document?.Name ?? "";

            return Page();
        }
        catch (RuleException error)
        {
            return AccessFailure(error);
        }
    }

    public async Task<IActionResult> OnPostAsync(
        Guid id,
        Guid offerId,
        Guid? documentId,
        IFormFile? upload
    )
    {
        try
        {
            await LoadAsync(id, offerId, documentId);

            if (!ModelState.IsValid || upload is null || upload.Length == 0)
            {
                throw new RuleException("InvalidDocument");
            }

            if (upload.Length > DocumentFile.MaximumBytes)
            {
                throw new RuleException("DocumentTooLarge");
            }

            await using var input = upload.OpenReadStream();
            await documents.UploadAsync(
                id,
                Version,
                offerId,
                documentId,
                Name,
                input,
                upload.FileName,
                Note,
                HttpContext.RequestAborted
            );

            return RedirectToDocuments(id, offerId);
        }
        catch (RuleException error)
        {
            if (error.Message is ForbiddenCode or NotFoundCode)
            {
                return AccessFailure(error);
            }

            return Rejected(error);
        }
    }

    private bool IsHtmxRequest => Request.Headers.ContainsKey("HX-Request");

    private IActionResult RedirectToDocuments(Guid id, Guid offerId)
    {
        var route = new
        {
            id,
            offerId,
            first = First,
            second = Second,
        };

        // The form posts with HTMX and swaps nothing; the header makes the browser navigate.
        if (IsHtmxRequest)
        {
            Response.Headers["HX-Redirect"] = Url.Page(
                "Details",
                null,
                route,
                null,
                null,
                DocumentsFragment
            );

            return new EmptyResult();
        }

        return RedirectToPage("Details", null, route, DocumentsFragment);
    }

    private IActionResult Rejected(RuleException error)
    {
        ModelState.AddModelError("", text[error.Message]);
        Response.StatusCode =
            error.Message == ConflictCode
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status422UnprocessableEntity;

        if (IsHtmxRequest)
        {
            return new JsonResult(
                new
                {
                    error = text[error.Message].Value,
                    version = Evaluation.Version,
                    reviewUrl = ReturnUrl,
                    context = $"{Offer.Latest.Terms.Name} · {text["OfferVersion", Offer.Latest.Number]}",
                }
            );
        }

        // A full page reload loses the chosen file, so the reader is asked to select it again.
        ModelState.AddModelError("", text["DocumentReselect"]);

        return Page();
    }

    private StatusCodeResult AccessFailure(RuleException error)
    {
        return StatusCode(
            error.Message == ForbiddenCode
                ? StatusCodes.Status403Forbidden
                : StatusCodes.Status404NotFound
        );
    }

    private async Task LoadAsync(Guid id, Guid offerId, Guid? documentId)
    {
        Evaluation = await evaluations.ReadAsync(id);
        Evaluation.Require(current.Id, AccessLevel.Editor);
        Offer = Evaluation.FindOffer(offerId);

        // The comparison to return to only ever holds offers for the same car as this one.
        var isFirstForSameCar = Evaluation.Offers.Any(item =>
            item.Id == First && item.CandidateId == Offer.CandidateId
        );
        First = isFirstForSameCar ? First : offerId;

        var isSecondForSameCar = Evaluation.Offers.Any(item =>
            item.Id == Second && item.Id != First && item.CandidateId == Offer.CandidateId
        );
        Second = isSecondForSameCar ? Second : null;

        if (documentId.HasValue)
        {
            Document = Evaluation.FindDocument(documentId.Value);

            // A document of another offer must not be reachable through this offer's address.
            if (Document.OfferId != offerId)
            {
                throw new RuleException(NotFoundCode);
            }
        }
    }
}
