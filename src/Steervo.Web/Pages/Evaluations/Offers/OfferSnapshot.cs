using Microsoft.Extensions.Localization;
using Steervo.Core;
using Steervo.Web.Pages.Shared;

namespace Steervo.Web.Pages.Evaluations.Offers;

/// <summary>
/// One version of an offer together with the currency its amounts are shown in. The criteria are
/// passed where price lines name the requirement they fulfil.
/// </summary>
public sealed record OfferSnapshot(
    OfferRevision Revision,
    string? Currency,
    IReadOnlyList<Criterion>? Criteria = null
)
{
    /// <summary>Leasing and financing split the price into a financed part and a part due at handover.</summary>
    public bool IsContract => Revision.Terms.Kind is OfferKind.Leasing or OfferKind.Financing;

    public IEnumerable<OfferLine> OrderedLines => Revision.Lines.OrderBy(line => line.Position);

    /// <summary>
    /// Whether a group of values is listed in the offer record. Prices are shown with the price list
    /// instead, and empty notes or a contract part that does not apply are left out.
    /// </summary>
    public bool ShowsValueGroup(string group)
    {
        if (group == OfferFields.PriceGroup)
        {
            return false;
        }

        if (group == OfferFields.NotesGroup && string.IsNullOrEmpty(Revision.Terms.Notes))
        {
            return false;
        }

        return OfferFields.GroupApplies(group, Revision.Terms);
    }

    /// <summary>Formats an amount with its sign and the offer currency.</summary>
    public string Money(decimal amount)
    {
        return $"{OfferPrices.Format(amount)} {Currency}";
    }

    /// <summary>The price of a line; a discount is shown as a negative amount, any other pricing by name.</summary>
    public string LinePrice(OfferLine line, IStringLocalizer text)
    {
        if (line.PriceKind != OfferPriceKind.Amount)
        {
            return text["OfferPriceKind" + line.PriceKind].Value;
        }

        var signedAmount =
            line.Kind == OfferLineKind.Discount ? -line.Amount!.Value : line.Amount!.Value;

        return Money(signedAmount);
    }

    /// <summary>The requirement a line fulfils, when it names one that is still known here.</summary>
    public Criterion? CriterionOf(OfferLine line)
    {
        if (!line.CriterionId.HasValue)
        {
            return null;
        }

        return Criteria?.FirstOrDefault(criterion => criterion.Id == line.CriterionId);
    }

    /// <summary>A line is paid at handover when a contract finances the rest but not this amount.</summary>
    public bool IsDueAtHandover(OfferLine line)
    {
        return IsContract && line.PriceKind == OfferPriceKind.Amount && !line.Financed;
    }

    public static bool HasBenefitDetails(OfferLine line)
    {
        var hasDetails =
            line.Effect.HasValue || line.CostCategory.HasValue || line.DurationMonths.HasValue;

        return line.Kind == OfferLineKind.Benefit && hasDetails;
    }
}

/// <summary>One requirement of an evaluation as it is answered for one offer.</summary>
public sealed record OfferAssessmentRow(
    Evaluation Evaluation,
    Offer Offer,
    Criterion Criterion,
    AccessLevel Access,
    AssessmentDraft? Draft = null,
    string? ReturnContext = null
)
{
    private const string UnknownAnswer = "FulfillmentUnknown";

    public OfferAssessment? Assessment =>
        Evaluation.OfferAssessments.SingleOrDefault(value =>
            value.OfferId == Offer.Id && value.CriterionId == Criterion.Id
        );

    /// <summary>An answer given for an earlier version of the offer has to be reviewed.</summary>
    public bool Stale => Assessment is not null && Assessment.RevisionNumber != Offer.Latest.Number;

    public Fulfillment Result => Evaluation.OfferResult(Offer.Id, Criterion.Id);

    /// <summary>The CSS class and <c>data-result</c> value of the result.</summary>
    public string ResultClass => Result.ToString().ToLowerInvariant();

    /// <summary>The resource key of the result where space is tight; a rating has a shorter wording there.</summary>
    public string ShortResultKey =>
        Result == Fulfillment.Rated ? "FulfillmentRatedShort" : "Fulfillment" + Result;

    // The line of the current version that fulfils this requirement, unless an own answer replaces it.
    public OfferLine? LinkedLine =>
        Assessment is { HasValue: true } && !Stale
            ? null
            : Evaluation.OfferLineFor(Offer, Criterion);

    public string FieldId => "offer-assessment-" + Criterion.Id;

    public string PostUrl => $"/evaluations/{Evaluation.Id}/offers/{Offer.Id}{ReturnContext}";

    public bool HasDraft => Draft?.SubjectId == Offer.Id && Draft.CriterionId == Criterion.Id;

    public bool CanEdit => Access >= AccessLevel.Editor;

    /// <summary>
    /// The answer as shown to the reader: a price line that fulfils the requirement counts as "yes",
    /// otherwise the own answer in the form of the criterion type, or "unknown" without an answer.
    /// </summary>
    public string AnswerText(IStringLocalizer text)
    {
        if (LinkedLine is not null)
        {
            return text["Yes"].Value;
        }

        var assessment = Assessment;
        switch (Criterion.Type)
        {
            case CriterionType.Boolean:
                return text[BooleanAnswerKey(assessment)].Value;

            case CriterionType.Rating:
                return assessment?.RatingValue is { } rating
                    ? $"{rating} / 5"
                    : text[UnknownAnswer].Value;

            default:
                return assessment?.MeasurementValue is { } measurement
                    ? $"{measurement:G} {Criterion.Unit}"
                    : text[UnknownAnswer].Value;
        }
    }

    /// <summary>
    /// The inline editor for this answer. An answer given for an earlier version is offered for
    /// review: the editor names that version and its save button confirms the answer.
    /// </summary>
    public AssessmentEditor Editor(IStringLocalizer text)
    {
        var assessment = Assessment;
        var stale = Stale;

        return new AssessmentEditor(
            Evaluation,
            Criterion,
            Offer.Id,
            Offer.Latest.Terms.Name,
            "offerId",
            "assess-offer",
            FieldId,
            PostUrl
        )
        {
            BooleanValue = assessment?.BooleanValue,
            OptionalAvailable = assessment?.OptionalAvailable ?? false,
            MeasurementValue = assessment?.MeasurementValue,
            RatingValue = assessment?.RatingValue,
            Source = assessment?.Source ?? AssessmentSource.Unknown,
            Notes = assessment?.Notes,
            HasAssessment = assessment is not null,
            Draft = HasDraft ? Draft : null,
            ReviewNotice = stale
                ? text["OfferAssessmentStale", assessment!.RevisionNumber].Value
                : null,
            SubmitLabel = stale ? text["OfferAssessmentReview"].Value : null,
        };
    }

    private static string BooleanAnswerKey(OfferAssessment? assessment)
    {
        if (assessment?.OptionalAvailable == true)
        {
            return "BooleanOptional";
        }

        if (assessment?.BooleanValue is { } fulfilled)
        {
            return fulfilled ? "Yes" : "No";
        }

        return UnknownAnswer;
    }
}

/// <summary>The offer requirements of one criterion group; a null group holds the ungrouped ones.</summary>
public sealed record OfferAssessmentGroup(CriterionGroup? Group, IReadOnlyList<Criterion> Criteria);

/// <summary>Names who sells, who finances and who arranged an offer in one line.</summary>
public static class OfferParties
{
    public static string? Summary(OfferTerms terms, IStringLocalizer text)
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(terms.Provider))
        {
            parts.Add(terms.Provider);
        }

        // A provider that finances or arranges its own offer is not named twice.
        if (!string.IsNullOrEmpty(terms.Lender) && terms.Lender != terms.Provider)
        {
            parts.Add(text["OfferFinancedBy", terms.Lender]);
        }

        if (!string.IsNullOrEmpty(terms.Intermediary) && terms.Intermediary != terms.Provider)
        {
            parts.Add(text["OfferArrangedBy", terms.Intermediary]);
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }
}

/// <summary>The rows of the price summary of an offer version and how their amounts are derived.</summary>
public static class OfferPrices
{
    public const string Vehicle = "Vehicle";
    public const string Extras = "Extras";
    public const string Discounts = "Discounts";
    public const string Total = "Total";
    public const string Financed = "Financed";
    public const string DueAtHandover = "DueAtHandover";
    public const string BenefitValue = "BenefitValue";
    public const string Concession = "Concession";

    public static readonly string[] Keys =
    [
        Vehicle,
        Extras,
        Discounts,
        Total,
        Financed,
        DueAtHandover,
        BenefitValue,
        Concession,
    ];

    // Null means the row does not apply or cannot be calculated yet.
    public static decimal? Value(OfferRevision revision, string key)
    {
        var pricing = revision.Pricing;
        var contract = revision.Terms.Kind is OfferKind.Leasing or OfferKind.Financing;

        // The value of included benefits is known even while the price itself is still open.
        if (key == BenefitValue)
        {
            return pricing.BenefitValue > 0 ? pricing.BenefitValue : null;
        }

        if (!pricing.Known)
        {
            return null;
        }

        return key switch
        {
            Vehicle => pricing.Vehicle,
            Extras => pricing.Extras,
            Discounts => -pricing.Discounts,
            Total => pricing.Total,
            Financed => contract ? pricing.Financed : null,
            DueAtHandover => contract ? pricing.DueAtHandover : null,
            Concession => pricing.Concession > 0 ? pricing.Concession : null,
            _ => null,
        };
    }

    public static string Label(OfferRevision revision, string key)
    {
        var isLoan = key == Financed && revision.Terms.Kind == OfferKind.Financing;

        return isLoan ? "OfferPriceFinancedLoan" : "OfferPrice" + key;
    }

    /// <summary>
    /// A row of the price summary is left out when it has no amount, and extras or discounts also
    /// when they add up to nothing.
    /// </summary>
    public static bool IsListed(string key, decimal? value)
    {
        if (value is null)
        {
            return false;
        }

        var isEmptySum = value == 0 && key is Extras or Discounts;

        return !isEmptySum;
    }

    public static string Format(decimal value)
    {
        return (value < 0 ? "−" : "") + Math.Abs(value).ToString("N2");
    }
}
