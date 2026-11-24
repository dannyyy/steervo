using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Steervo.Application.Documents;
using Steervo.Application.Evaluations;
using Steervo.Application.Offers;
using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations.Offers;

/// <summary>
/// Creates an offer or records a new version of an existing one, optionally together with the PDF
/// the values were taken from.
/// </summary>
[RequestSizeLimit(DocumentFile.MaximumBytes + 1_000_000)]
[RequestFormLimits(MultipartBodyLengthLimit = DocumentFile.MaximumBytes)]
public sealed class EditModel(
    OfferWorkspace workspace,
    ICurrentUser current,
    IStringLocalizer<Ui> text,
    IDocumentStorage documents
) : PageModel
{
    /// <summary>The placeholder key of the line template; the browser replaces it for every added line.</summary>
    public const string NewLineKey = "__index__";

    private const string ForbiddenCode = "Forbidden";
    private const string NotFoundCode = "NotFound";
    private const string ConflictCode = "Conflict";
    private const string InvalidDocumentCode = "InvalidDocument";

    public Evaluation Evaluation { get; private set; } = null!;

    /// <summary>The offer being revised; null while a new offer is created.</summary>
    public Offer? Offer { get; private set; }

    public AccessLevel Access => Evaluation.AccessFor(current.Id)!.Value;

    [BindProperty]
    public OfferForm Input { get; set; } = new();

    [BindProperty]
    public long Version { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? First { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? Second { get; set; }

    public string ReturnUrl =>
        Offer is null
            ? $"/evaluations/{Evaluation.Id}/offers"
            : $"/evaluations/{Evaluation.Id}/offers/{Offer.Id}?first={First}&second={Second}";

    public string? Notice { get; private set; }

    /// <summary>The yes/no offer requirements a price line can be named to fulfil.</summary>
    public IReadOnlyList<Criterion> LineCriteria =>
        Evaluation
            .Criteria.Where(criterion =>
                criterion is { Scope: CriterionScope.Offer, Type: CriterionType.Boolean }
            )
            .OrderBy(criterion => criterion.Position)
            .ToList();

    public bool IsNew => Offer is null;

    public string TitleKey => IsNew ? "NewOffer" : "OfferEdit";

    /// <summary>Names what is being edited: the evaluation for a new offer, otherwise the offer.</summary>
    public string EditorContext => Offer is null ? Evaluation.Name : Offer.Latest.Terms.Name;

    public IEnumerable<Candidate> OrderedCandidates =>
        Evaluation.Candidates.OrderBy(candidate => candidate.Position);

    /// <summary>The editors of the entered price lines; each is keyed by its position in the form.</summary>
    public IEnumerable<OfferLineEditor> LineEditors
    {
        get
        {
            var criteria = LineCriteria;

            return Input.Lines.Select(
                (line, index) => new OfferLineEditor(line, index.ToString(), criteria)
            );
        }
    }

    /// <summary>The blank line the browser copies when a price line is added.</summary>
    public OfferLineEditor LineTemplate
    {
        get
        {
            var blankLine = new OfferLineForm
            {
                PriceKind = OfferPriceKind.Amount,
                Financed = true,
            };

            return new OfferLineEditor(blankLine, NewLineKey, LineCriteria);
        }
    }

    public async Task<IActionResult> OnGetAsync(Guid id, Guid? offerId, Guid? candidateId)
    {
        try
        {
            await LoadAsync(id, offerId);

            // Offers are entered in the currency of the evaluation, so that has to be chosen first.
            if (Evaluation.Currency is null)
            {
                return RedirectToPage("/Evaluations/Details", new { id, section = "settings" });
            }

            Version = Evaluation.Version;

            if (Offer is not null)
            {
                Input = OfferForm.From(Offer, Evaluation.Currency);
            }
            else
            {
                PrepareNewOffer(candidateId);
            }

            return Page();
        }
        catch (RuleException error)
        {
            return AccessFailure(error);
        }
    }

    public async Task<IActionResult> OnPostAsync(Guid id, Guid? offerId, IFormFile? upload)
    {
        try
        {
            await LoadAsync(id, offerId);

            if (!ModelState.IsValid)
            {
                throw new RuleException("InvalidOfferTerms");
            }

            var terms = Input.ToTerms();
            var lines = Input.Lines.Select(line => line.ToInput()).ToArray();

            if (Evaluation.Version != Version)
            {
                throw new RuleException(ConflictCode);
            }

            if (Offer is null)
            {
                _ = Evaluation.FindCandidate(Input.CandidateId);
            }

            // The file is stored only after the form itself was accepted, and before the offer is saved.
            DocumentFile? attachment = null;
            if (upload is not null)
            {
                attachment = await StoreAttachmentAsync(upload);
            }

            if (Offer is null)
            {
                offerId = await workspace.AddAsync(
                    id,
                    Version,
                    Input.CandidateId,
                    terms,
                    lines,
                    Input.Currency,
                    Input.ChangeNote,
                    attachment
                );
            }
            else
            {
                await workspace.ReviseAsync(
                    id,
                    Version,
                    Offer.Id,
                    terms,
                    lines,
                    Input.ChangeNote,
                    attachment
                );
            }

            return RedirectToOffer(id, offerId);
        }
        catch (RuleException error)
        {
            if (error.Message is ForbiddenCode or NotFoundCode)
            {
                return AccessFailure(error);
            }

            return Rejected(error, upload);
        }
    }

    private bool IsHtmxRequest => Request.Headers.ContainsKey("HX-Request");

    private void PrepareNewOffer(Guid? candidateId)
    {
        Input.Currency = Evaluation.Currency;

        if (candidateId.HasValue)
        {
            var candidate = Evaluation.FindCandidate(candidateId.Value);
            Input.CandidateId = candidate.Id;
            Input.VehicleDescription = candidate.DisplayName;
        }
    }

    private async Task<DocumentFile> StoreAttachmentAsync(IFormFile upload)
    {
        if (upload.Length == 0 || !HasPdfExtension(upload))
        {
            throw new RuleException(InvalidDocumentCode);
        }

        if (upload.Length > DocumentFile.MaximumBytes)
        {
            throw new RuleException("DocumentTooLarge");
        }

        // The file name alone proves nothing; a PDF starts with this signature.
        await using (var probe = upload.OpenReadStream())
        {
            var header = new byte[5];
            var count = await probe.ReadAtLeastAsync(
                header,
                header.Length,
                false,
                HttpContext.RequestAborted
            );
            if (count != header.Length || !header.AsSpan().SequenceEqual("%PDF-"u8))
            {
                throw new RuleException(InvalidDocumentCode);
            }
        }

        await using var stream = upload.OpenReadStream();

        return await documents.SaveAsync(stream, upload.FileName, HttpContext.RequestAborted);
    }

    private static bool HasPdfExtension(IFormFile upload)
    {
        return upload.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    private IActionResult RedirectToOffer(Guid id, Guid? offerId)
    {
        var route = new
        {
            id,
            offerId,
            first = First,
            second = Second,
        };

        // The editor posts with HTMX and swaps nothing; the header makes the browser navigate.
        if (IsHtmxRequest)
        {
            Response.Headers["HX-Redirect"] = Url.Page("Details", route);

            return new EmptyResult();
        }

        return RedirectToPage("Details", route);
    }

    private IActionResult Rejected(RuleException error, IFormFile? upload)
    {
        Notice = text[error.Message];

        // A full page reload loses the chosen file; the HTMX editor keeps it.
        if (!IsHtmxRequest && upload is not null)
        {
            Notice += " " + text["DocumentReselect"];
        }

        Response.StatusCode =
            error.Message == ConflictCode
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status422UnprocessableEntity;

        if (IsHtmxRequest)
        {
            return new JsonResult(
                new
                {
                    error = Notice,
                    version = Evaluation.Version,
                    reviewUrl = ReturnUrl,
                    context = Offer is null
                        ? Evaluation.Name
                        : $"{Offer.Latest.Terms.Name} · {text["OfferVersion", Offer.Latest.Number]}",
                }
            );
        }

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

    private async Task LoadAsync(Guid id, Guid? offerId)
    {
        Evaluation = await workspace.ReadAsync(id);
        Evaluation.Require(current.Id, AccessLevel.Editor);
        Offer = offerId.HasValue ? Evaluation.FindOffer(offerId.Value) : null;

        // The comparison to return to only ever holds offers for the same car as the edited one.
        var isFirstForSameCar = Evaluation.Offers.Any(item =>
            item.Id == First && item.CandidateId == Offer?.CandidateId
        );
        First = isFirstForSameCar ? First : Offer?.Id;

        var isSecondForSameCar = Evaluation.Offers.Any(item =>
            item.Id == Second && item.Id != First && item.CandidateId == Offer?.CandidateId
        );
        Second = isSecondForSameCar ? Second : null;
    }
}
