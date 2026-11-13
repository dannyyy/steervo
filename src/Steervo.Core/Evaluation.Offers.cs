namespace Steervo.Core;

public sealed partial class Evaluation
{
    public string? Currency { get; private set; }
    public List<Offer> Offers { get; private set; } = [];
    public List<OfferAssessment> OfferAssessments { get; private set; } = [];

    public void SetCurrency(string currency)
    {
        var code = ValidateCurrency(currency);
        if (Currency is not null && Currency != code) throw new RuleException("CurrencyFixed");
        Currency = code;
    }
    private static string ValidateCurrency(string? currency)
    {
        var code = currency?.Trim().ToUpperInvariant() ?? "";
        if (code.Length != 3 || code.Any(character => character is < 'A' or > 'Z')) throw new RuleException("CurrencyRequired");
        return code;
    }
    public Offer FindOffer(Guid id) => Offers.SingleOrDefault(offer => offer.Id == id) ?? throw new RuleException("NotFound");
    public Guid AddOffer(Guid candidateId, OfferTerms terms, IEnumerable<OfferLineInput> lines, string? currency = null,
        string? changeNote = null, DateTimeOffset? createdAt = null)
    {
        _ = FindCandidate(candidateId);
        var code = ValidateCurrency(currency ?? Currency);
        if (Currency is not null && Currency != code) throw new RuleException("CurrencyFixed");
        var offer = new Offer(Id, candidateId, terms, lines, changeNote, createdAt);
        Currency = code; Offers.Add(offer);
        return offer.Id;
    }
    public void ReviseOffer(Guid id, OfferTerms terms, IEnumerable<OfferLineInput> lines, string? changeNote = null, DateTimeOffset? createdAt = null) =>
        FindOffer(id).Revise(terms, lines, changeNote, createdAt);
    public void ArchiveOffer(Guid id, bool archived) => FindOffer(id).SetArchived(archived);
    public void AssessOffer(Guid offerId, Guid criterionId, AssessmentInput input)
    {
        var offer = FindOffer(offerId);
        if (offer.Archived) throw new RuleException("OfferArchived");
        var criterion = FindCriterion(criterionId);
        var assessment = OfferAssessments.SingleOrDefault(value => value.OfferId == offerId && value.CriterionId == criterionId);
        if (assessment is null) OfferAssessments.Add(new OfferAssessment(Id, offer, criterion, input));
        else assessment.Update(offer, criterion, input);
    }
    public void ClearOfferAssessment(Guid offerId, Guid criterionId)
    {
        if (FindOffer(offerId).Archived) throw new RuleException("OfferArchived");
        _ = FindCriterion(criterionId);
        OfferAssessments.RemoveAll(value => value.OfferId == offerId && value.CriterionId == criterionId);
    }
    public Fulfillment OfferResult(Guid offerId, Guid criterionId)
    {
        var offer = FindOffer(offerId); var criterion = FindCriterion(criterionId);
        var assessment = OfferAssessments.SingleOrDefault(value => value.OfferId == offerId && value.CriterionId == criterionId);
        return assessment?.RevisionNumber == offer.Latest.Number
            ? criterion.Assess(assessment.BooleanValue, assessment.MeasurementValue, assessment.RatingValue, assessment.OptionalAvailable) : Fulfillment.Unknown;
    }
}

public sealed class OfferAssessment
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EvaluationId { get; private set; }
    public Guid OfferId { get; private set; }
    public Guid CriterionId { get; private set; }
    public int RevisionNumber { get; private set; }
    public bool? BooleanValue { get; private set; }
    public bool OptionalAvailable { get; private set; }
    public decimal? MeasurementValue { get; private set; }
    public int? RatingValue { get; private set; }
    public AssessmentSource Source { get; private set; }
    public string Notes { get; private set; } = "";
    public bool HasValue => OptionalAvailable || BooleanValue.HasValue || MeasurementValue.HasValue || RatingValue.HasValue;
    private OfferAssessment() { }
    internal OfferAssessment(Guid evaluationId, Offer offer, Criterion criterion, AssessmentInput input)
    {
        EvaluationId = evaluationId; OfferId = offer.Id; CriterionId = criterion.Id;
        Update(offer, criterion, input);
    }
    internal void Update(Offer offer, Criterion criterion, AssessmentInput input)
    {
        if (criterion.Scope != CriterionScope.Offer) throw new RuleException("OfferCriteriaOnly");
        if ((criterion.Type != CriterionType.Boolean && (input.BooleanValue.HasValue || input.OptionalAvailable))
            || (input.OptionalAvailable && input.BooleanValue.HasValue)
            || (criterion.Type != CriterionType.Measurement && input.MeasurementValue.HasValue)
            || (criterion.Type != CriterionType.Rating && input.RatingValue.HasValue)
            || input.RatingValue is < 1 or > 5) throw new RuleException("InvalidValue");
        var source = Rules.Defined(input.Source); var notes = Rules.OptionalText(input.Notes, 4000);
        BooleanValue = input.BooleanValue; OptionalAvailable = input.OptionalAvailable; MeasurementValue = input.MeasurementValue; RatingValue = input.RatingValue;
        Source = source; Notes = notes; RevisionNumber = offer.Latest.Number;
    }
    public void RestoreRevision(Offer offer, int revisionNumber)
    {
        if (offer.Id != OfferId) throw new RuleException("InvalidImport");
        _ = offer.Revision(revisionNumber);
        RevisionNumber = revisionNumber;
    }
}
