using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Steervo.Application.Evaluations;
using Steervo.Application.Offers;
using Steervo.Core;
using Steervo.Web.Pages.Evaluations.Candidates;
using Steervo.Web.Pages.Shared;

namespace Steervo.Web.Pages.Evaluations.Offers;

/// <summary>
/// The record of one offer: its values, price lines, differences to the previous version, the answers
/// to the offer requirements, its documents and its history.
/// </summary>
public sealed class DetailsModel(
    OfferWorkspace workspace,
    ICurrentUser current,
    IStringLocalizer<Ui> text
) : PageModel
{
    private const string RemoveOfferAction = "remove-offer";
    private const string AssessOfferAction = "assess-offer";
    private const string ClearAssessmentAction = "clear-assessment";
    private const string RemoveDocumentAction = "remove-document";

    private const string ForbiddenCode = "Forbidden";
    private const string NotFoundCode = "NotFound";
    private const string ConflictCode = "Conflict";
    private const string InvalidValueCode = "InvalidValue";

    // An empty "second" value in the query means the reader removed the second offer on purpose.
    private const string SecondSlot = "second";
    private const string TimestampFormat = "dd.MM.yyyy HH:mm 'UTC'";

    public Evaluation Evaluation { get; private set; } = null!;

    public Offer Offer { get; private set; } = null!;

    /// <summary>The shown version; older versions are read-only.</summary>
    public OfferRevision Revision { get; private set; } = null!;

    public bool Current => Revision.Number == Offer.Latest.Number;

    public AccessLevel Access => Evaluation.AccessFor(current.Id)!.Value;

    public string? Notice { get; private set; }

    public bool Failed { get; private set; }

    public AssessmentDraft? Draft { get; private set; }

    [BindProperty(SupportsGet = true)]
    public Guid? First { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? Second { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid[]? More { get; set; }

    /// <summary>The comparison the reader came from, as a query string that is appended to links and posts.</summary>
    public string ReturnContext =>
        $"?first={First}&second={Second}{ComparisonSelection.More(More)}";

    public Candidate Candidate => Evaluation.FindCandidate(Offer.CandidateId);

    public bool CanEdit => Access >= AccessLevel.Editor;

    /// <summary>Only the current version of an offer can be changed.</summary>
    public bool CanChange => CanEdit && Current;

    public string OfferUrl => $"/evaluations/{Evaluation.Id}/offers/{Offer.Id}";

    public IReadOnlyList<Criterion> OfferCriteria =>
        Evaluation.Criteria.Where(criterion => criterion.Scope == CriterionScope.Offer).ToList();

    /// <summary>The version before the shown one; null for the first version.</summary>
    public OfferRevision? PreviousRevision =>
        Revision.Number > 1 ? Offer.Revision(Revision.Number - 1) : null;

    /// <summary>
    /// The offer requirements by criterion group, followed by the ungrouped ones (group null). Groups
    /// without offer requirements are left out.
    /// </summary>
    public IEnumerable<OfferAssessmentGroup> AssessmentGroups
    {
        get
        {
            var groups = Evaluation
                .Groups.OrderBy(group => group.Position)
                .Cast<CriterionGroup?>()
                .Append(null);

            foreach (var group in groups)
            {
                var criteria = Evaluation
                    .Criteria.Where(criterion =>
                        criterion.Scope == CriterionScope.Offer && criterion.GroupId == group?.Id
                    )
                    .OrderBy(criterion => criterion.Position)
                    .ToList();

                if (criteria.Count > 0)
                {
                    yield return new OfferAssessmentGroup(group, criteria);
                }
            }
        }
    }

    /// <summary>The documents of the offer that already existed at the shown version, by name.</summary>
    public IReadOnlyList<OfferDocument> Documents =>
        Evaluation
            .Documents.Where(document =>
                document.OfferId == Offer.Id && document.Revisions.Any(ExistedAtShownVersion)
            )
            .OrderBy(document => document.Name)
            .ToArray();

    /// <summary>The terms whose value differs between the given earlier version and the shown one.</summary>
    public IEnumerable<OfferField> ChangedFields(OfferRevision previous)
    {
        return OfferFields.All.Where(field =>
            !Equals(field.Raw(previous.Terms), field.Raw(Revision.Terms))
        );
    }

    public bool LinesChangedSince(OfferRevision previous)
    {
        var before = previous.Lines.OrderBy(line => line.Position).Select(line => line.ToInput());
        var after = Revision.Lines.OrderBy(line => line.Position).Select(line => line.ToInput());

        return !before.SequenceEqual(after);
    }

    /// <summary>The versions of a document up to the shown offer version, newest first.</summary>
    public IReadOnlyList<DocumentRevision> HistoryOf(OfferDocument document)
    {
        return document
            .Revisions.Where(ExistedAtShownVersion)
            .OrderByDescending(revision => revision.Number)
            .ToArray();
    }

    public static string FormatTimestamp(DateTimeOffset timestamp)
    {
        return timestamp.ToString(TimestampFormat);
    }

    public static string FormatKibibytes(long bytes)
    {
        return (bytes / 1024m).ToString("N1");
    }

    public async Task<IActionResult> OnGetAsync(Guid id, Guid offerId, int? revision)
    {
        try
        {
            await LoadAsync(id, offerId, revision);

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
        int? revision,
        long version,
        string action,
        Guid criterionId,
        string? booleanValue,
        string? measurementValue,
        int? ratingValue,
        AssessmentSource source,
        string? notes,
        Guid documentId
    )
    {
        try
        {
            await LoadAsync(id, offerId, revision);
            Evaluation.Require(current.Id, AccessLevel.Editor);

            if (!ModelState.IsValid || !Current)
            {
                throw new RuleException(InvalidValueCode);
            }

            switch (action)
            {
                case RemoveOfferAction:
                    await workspace.RemoveAsync(id, version, offerId);
                    return RedirectToOfferList(id);

                case AssessOfferAction:
                    var assessment = AssessmentForm.ToInput(
                        booleanValue,
                        measurementValue,
                        ratingValue,
                        source,
                        notes
                    );
                    await workspace.AssessAsync(id, version, offerId, criterionId, assessment);
                    break;

                case ClearAssessmentAction:
                    await workspace.ClearAssessmentAsync(id, version, offerId, criterionId);
                    break;

                case RemoveDocumentAction:
                    await workspace.RemoveDocumentAsync(id, version, offerId, documentId);
                    break;

                default:
                    throw new RuleException(InvalidValueCode);
            }

            Notice = text["Saved"];
        }
        catch (RuleException error)
        {
            if (error.Message is ForbiddenCode or NotFoundCode)
            {
                return AccessFailure(error);
            }

            Failed = true;
            Notice = text[error.Message];

            // A rejected answer is kept as submitted, so its editor opens again with the input.
            if (action is AssessOfferAction or ClearAssessmentAction)
            {
                Draft = new(
                    offerId,
                    criterionId,
                    Request.Form["booleanValue"],
                    measurementValue,
                    Request.Form["ratingValue"],
                    Request.Form["source"],
                    notes,
                    Notice
                );
            }

            Response.StatusCode =
                error.Message == ConflictCode
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status422UnprocessableEntity;
        }

        await LoadAsync(id, offerId, revision);

        return IsHtmxRequest ? Partial("_OfferDetails", this) : Page();
    }

    private bool IsHtmxRequest => Request.Headers.ContainsKey("HX-Request");

    private bool ExistedAtShownVersion(DocumentRevision revision)
    {
        return revision.OfferRevisionNumber <= Revision.Number;
    }

    private IActionResult RedirectToOfferList(Guid id)
    {
        // HTMX follows a redirect inside the swapped fragment; the header makes it navigate instead.
        if (IsHtmxRequest)
        {
            Response.Headers["HX-Redirect"] = Url.Page("Index", new { id });

            return new EmptyResult();
        }

        return RedirectToPage("Index", new { id });
    }

    private StatusCodeResult AccessFailure(RuleException error)
    {
        return StatusCode(
            error.Message == ForbiddenCode
                ? StatusCodes.Status403Forbidden
                : StatusCodes.Status404NotFound
        );
    }

    private async Task LoadAsync(Guid id, Guid offerId, int? revision)
    {
        Evaluation = await workspace.ReadAsync(id);
        Offer = Evaluation.FindOffer(offerId);
        Revision = revision.HasValue ? Offer.Revision(revision.Value) : Offer.Latest;

        First = IsOfferForSameCar(First) ? First : offerId;
        Second = ResolveSecond();
        More = (More ?? [])
            .Distinct()
            .Where(item =>
                item != First && item != Second && Evaluation.Offers.Any(other => other.Id == item)
            )
            .Take(ComparisonSelection.Maximum - 2)
            .ToArray();
    }

    private bool IsOfferForSameCar(Guid? offerId)
    {
        return Evaluation.Offers.Any(item =>
            item.Id == offerId && item.CandidateId == Offer.CandidateId
        );
    }

    /// <summary>
    /// The offer shown next to this one in a comparison: the requested one when it is another offer for
    /// the same car, otherwise the first other offer for that car. An explicitly empty slot stays empty.
    /// </summary>
    private Guid? ResolveSecond()
    {
        if (Second != First && IsOfferForSameCar(Second))
        {
            return Second;
        }

        if (Request.Query.ContainsKey(SecondSlot))
        {
            return null;
        }

        return Evaluation
            .Offers.OrderBy(item => item.Position)
            .FirstOrDefault(item => item.CandidateId == Offer.CandidateId && item.Id != First)
            ?.Id;
    }
}
