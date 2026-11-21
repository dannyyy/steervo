using Steervo.Web.Pages.Shared;
using Steervo.Core;
namespace Steervo.Web.Pages.Evaluations.Offers;
public sealed record OfferSnapshot(OfferRevision Revision, string? Currency, IReadOnlyList<Criterion>? Criteria = null);
public sealed record OfferAssessmentRow(Evaluation Evaluation, Offer Offer, Criterion Criterion, AccessLevel Access,
    AssessmentDraft? Draft = null, string? ReturnContext = null)
{
    public OfferAssessment? Assessment => Evaluation.OfferAssessments.SingleOrDefault(value => value.OfferId == Offer.Id && value.CriterionId == Criterion.Id);
    public bool Stale => Assessment is not null && Assessment.RevisionNumber != Offer.Latest.Number;
    public Fulfillment Result => Evaluation.OfferResult(Offer.Id, Criterion.Id);
    // The line of the current version that fulfils this requirement, unless an own answer replaces it.
    public OfferLine? LinkedLine => Assessment is { HasValue: true } && !Stale ? null : Evaluation.OfferLineFor(Offer, Criterion);
    public string FieldId => "offer-assessment-" + Criterion.Id;
    public string PostUrl => $"/evaluations/{Evaluation.Id}/offers/{Offer.Id}{ReturnContext}";
    public bool HasDraft => Draft?.SubjectId == Offer.Id && Draft.CriterionId == Criterion.Id;
}
public static class OfferPrices
{
    public static readonly string[] Keys = ["Vehicle", "Extras", "Discounts", "Total", "Financed", "DueAtHandover", "BenefitValue", "Concession"];
    // Null means the row does not apply or cannot be calculated yet.
    public static decimal? Value(OfferRevision revision, string key)
    {
        var pricing = revision.Pricing; var contract = revision.Terms.Kind is OfferKind.Leasing or OfferKind.Financing;
        if (key == "BenefitValue") return pricing.BenefitValue > 0 ? pricing.BenefitValue : null;
        if (!pricing.Known) return null;
        return key switch
        {
            "Vehicle" => pricing.Vehicle, "Extras" => pricing.Extras, "Discounts" => -pricing.Discounts, "Total" => pricing.Total,
            "Financed" => contract ? pricing.Financed : null, "DueAtHandover" => contract ? pricing.DueAtHandover : null,
            "Concession" => pricing.Concession > 0 ? pricing.Concession : null, _ => null
        };
    }
    public static string Label(OfferRevision revision, string key) => key == "Financed" && revision.Terms.Kind == OfferKind.Financing ? "OfferPriceFinancedLoan" : "OfferPrice" + key;
    public static string Format(decimal value) => (value < 0 ? "−" : "") + Math.Abs(value).ToString("N2");
}
