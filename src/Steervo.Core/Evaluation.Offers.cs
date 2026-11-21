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
        var offer = new Offer(Id, candidateId, terms, Linked(lines), changeNote, createdAt) { Position = Offers.Count };
        Currency = code; Offers.Add(offer);
        return offer.Id;
    }
    public void ReviseOffer(Guid id, OfferTerms terms, IEnumerable<OfferLineInput> lines, string? changeNote = null, DateTimeOffset? createdAt = null) =>
        FindOffer(id).Revise(terms, Linked(lines), changeNote, createdAt);
    // A line may only satisfy a yes/no requirement that belongs to offers.
    private OfferLineInput[] Linked(IEnumerable<OfferLineInput> lines)
    {
        var result = lines.Take(501).ToArray();
        foreach (var line in result.Where(line => line.CriterionId.HasValue))
            if (Criteria.SingleOrDefault(criterion => criterion.Id == line.CriterionId) is not { Scope: CriterionScope.Offer, Type: CriterionType.Boolean })
                throw new RuleException("InvalidOfferLine");
        return result;
    }
    // Moves an offer past its neighbour within the listed offers, e.g. the offers of one car.
    public void MoveOffer(Guid id, int direction, Guid? candidateId = null)
    {
        if (direction is not (-1 or 1)) throw new RuleException("InvalidValue");
        var offer = FindOffer(id);
        var listed = Offers.Where(item => !candidateId.HasValue || item.CandidateId == candidateId).OrderBy(item => item.Position).ToList();
        var index = listed.IndexOf(offer) + direction;
        if (listed.IndexOf(offer) < 0 || index < 0 || index >= listed.Count) return;
        (offer.Position, listed[index].Position) = (listed[index].Position, offer.Position);
    }
    public void RemoveOffer(Guid id)
    {
        var offer = FindOffer(id);
        Scenarios.RemoveAll(scenario => scenario.OfferId == id);
        Documents.RemoveAll(document => document.OfferId == id);
        OfferAssessments.RemoveAll(assessment => assessment.OfferId == id);
        Offers.Remove(offer);
        NormalizeOffers();
    }
    private void NormalizeOffers()
    {
        var position = 0;
        foreach (var offer in Offers.OrderBy(offer => offer.Position)) offer.Position = position++;
    }
    public void AssessOffer(Guid offerId, Guid criterionId, AssessmentInput input)
    {
        var offer = FindOffer(offerId);
        var criterion = FindCriterion(criterionId);
        var assessment = OfferAssessments.SingleOrDefault(value => value.OfferId == offerId && value.CriterionId == criterionId);
        if (assessment is null) OfferAssessments.Add(new OfferAssessment(Id, offer, criterion, input));
        else assessment.Update(offer, criterion, input);
    }
    public void ClearOfferAssessment(Guid offerId, Guid criterionId)
    {
        _ = FindOffer(offerId); _ = FindCriterion(criterionId);
        OfferAssessments.RemoveAll(value => value.OfferId == offerId && value.CriterionId == criterionId);
    }
    public OfferLine? OfferLineFor(Offer offer, Criterion criterion) => criterion is { Scope: CriterionScope.Offer, Type: CriterionType.Boolean }
        ? offer.Latest.Lines.OrderBy(line => line.Position).FirstOrDefault(line => line.CriterionId == criterion.Id) : null;
    public Fulfillment OfferResult(Guid offerId, Guid criterionId)
    {
        var offer = FindOffer(offerId); var criterion = FindCriterion(criterionId);
        var assessment = OfferAssessments.SingleOrDefault(value => value.OfferId == offerId && value.CriterionId == criterionId);
        // An own answer for the current version wins; otherwise a linked line fulfils the requirement.
        if (!(assessment?.RevisionNumber == offer.Latest.Number && assessment.HasValue) && OfferLineFor(offer, criterion) is not null)
            return criterion.Assess(true, null, null, false);
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
