using Steervo.Core;
namespace Steervo.Web.Pages.Evaluations.Offers;
public sealed record OfferSnapshot(OfferRevision Revision, string? Currency);
public sealed record OfferAssessmentRow(Evaluation Evaluation, Offer Offer, Criterion Criterion, AccessLevel Access)
{
    public OfferAssessment? Assessment => Evaluation.OfferAssessments.SingleOrDefault(value => value.OfferId == Offer.Id && value.CriterionId == Criterion.Id);
    public bool Stale => Assessment is not null && Assessment.RevisionNumber != Offer.Latest.Number;
    public Fulfillment Result => Evaluation.OfferResult(Offer.Id, Criterion.Id);
    public string FieldId => "offer-assessment-" + Criterion.Id;
    public string PostUrl => $"/evaluations/{Evaluation.Id}/offers/{Offer.Id}";
}
