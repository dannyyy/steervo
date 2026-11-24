namespace Steervo.Core;

public sealed partial class Evaluation
{
    private const int CurrencyCodeLength = 3;

    public string? Currency { get; private set; }
    public List<Offer> Offers { get; private set; } = [];
    public List<OfferAssessment> OfferAssessments { get; private set; } = [];

    public void SetCurrency(string currency)
    {
        var code = ValidateCurrency(currency);
        RequireSameCurrency(code);

        Currency = code;
    }

    private static string ValidateCurrency(string? currency)
    {
        var code = currency?.Trim().ToUpperInvariant() ?? "";
        var hasOnlyLetters = code.All(character => character is >= 'A' and <= 'Z');

        if (code.Length != CurrencyCodeLength || !hasOnlyLetters)
        {
            throw new RuleException("CurrencyRequired");
        }

        return code;
    }

    // All amounts of an evaluation share one currency; once chosen it cannot be replaced.
    private void RequireSameCurrency(string code)
    {
        if (Currency is not null && Currency != code)
        {
            throw new RuleException("CurrencyFixed");
        }
    }

    public Offer FindOffer(Guid id) =>
        Offers.SingleOrDefault(offer => offer.Id == id) ?? throw new RuleException("NotFound");

    /// <summary>
    /// Records a first offer version for a car. The first offer also settles the currency of the
    /// evaluation when none is set yet.
    /// </summary>
    public Guid AddOffer(
        Guid candidateId,
        OfferTerms terms,
        IEnumerable<OfferLineInput> lines,
        string? currency = null,
        string? changeNote = null,
        DateTimeOffset? createdAt = null
    )
    {
        _ = FindCandidate(candidateId);
        var code = ValidateCurrency(currency ?? Currency);
        RequireSameCurrency(code);

        var linkedLines = Linked(lines);
        var offer = new Offer(Id, candidateId, terms, linkedLines, changeNote, createdAt)
        {
            Position = Offers.Count,
        };

        Currency = code;
        Offers.Add(offer);

        return offer.Id;
    }

    public void ReviseOffer(
        Guid id,
        OfferTerms terms,
        IEnumerable<OfferLineInput> lines,
        string? changeNote = null,
        DateTimeOffset? createdAt = null
    ) => FindOffer(id).Revise(terms, Linked(lines), changeNote, createdAt);

    // A line may only satisfy a yes/no requirement that belongs to offers.
    private OfferLineInput[] Linked(IEnumerable<OfferLineInput> lines)
    {
        // One line more than allowed is kept so that the offer version can still reject the excess.
        var result = lines.Take(OfferRevision.MaximumLines + 1).ToArray();

        foreach (var line in result.Where(line => line.CriterionId.HasValue))
        {
            var linked = Criteria.SingleOrDefault(criterion => criterion.Id == line.CriterionId);

            if (linked is not { Scope: CriterionScope.Offer, Type: CriterionType.Boolean })
            {
                throw new RuleException("InvalidOfferLine");
            }
        }

        return result;
    }

    // Moves an offer past its neighbour within the listed offers, e.g. the offers of one car.
    public void MoveOffer(Guid id, int direction, Guid? candidateId = null)
    {
        if (direction is not (-1 or 1))
        {
            throw new RuleException("InvalidValue");
        }

        var offer = FindOffer(id);
        var listed = Offers
            .Where(item => !candidateId.HasValue || item.CandidateId == candidateId)
            .OrderBy(item => item.Position)
            .ToList();
        var currentIndex = listed.IndexOf(offer);
        var neighbourIndex = currentIndex + direction;

        if (currentIndex < 0 || neighbourIndex < 0 || neighbourIndex >= listed.Count)
        {
            return;
        }

        var neighbour = listed[neighbourIndex];
        (offer.Position, neighbour.Position) = (neighbour.Position, offer.Position);
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

        foreach (var offer in Offers.OrderBy(offer => offer.Position))
        {
            offer.Position = position++;
        }
    }

    public void AssessOffer(Guid offerId, Guid criterionId, AssessmentInput input)
    {
        var offer = FindOffer(offerId);
        var criterion = FindCriterion(criterionId);
        var assessment = OfferAssessmentFor(offerId, criterionId);

        if (assessment is null)
        {
            OfferAssessments.Add(new OfferAssessment(Id, offer, criterion, input));
        }
        else
        {
            assessment.Update(offer, criterion, input);
        }
    }

    public void ClearOfferAssessment(Guid offerId, Guid criterionId)
    {
        _ = FindOffer(offerId);
        _ = FindCriterion(criterionId);

        OfferAssessments.RemoveAll(assessment =>
            assessment.OfferId == offerId && assessment.CriterionId == criterionId
        );
    }

    private OfferAssessment? OfferAssessmentFor(Guid offerId, Guid criterionId) =>
        OfferAssessments.SingleOrDefault(assessment =>
            assessment.OfferId == offerId && assessment.CriterionId == criterionId
        );

    /// <summary>The line of the current offer version that fulfils a yes/no offer criterion, if any.</summary>
    public OfferLine? OfferLineFor(Offer offer, Criterion criterion)
    {
        if (criterion is not { Scope: CriterionScope.Offer, Type: CriterionType.Boolean })
        {
            return null;
        }

        return offer
            .Latest.Lines.OrderBy(line => line.Position)
            .FirstOrDefault(line => line.CriterionId == criterion.Id);
    }

    /// <summary>
    /// The result of an offer criterion for the current offer version. Answers given for an
    /// earlier version no longer count.
    /// </summary>
    public Fulfillment OfferResult(Guid offerId, Guid criterionId)
    {
        var offer = FindOffer(offerId);
        var criterion = FindCriterion(criterionId);
        var assessment = OfferAssessmentFor(offerId, criterionId);
        var currentRevisionNumber = offer.Latest.Number;

        var hasOwnAnswer =
            assessment is not null
            && assessment.RevisionNumber == currentRevisionNumber
            && assessment.HasValue;

        // An own answer for the current version wins; otherwise a linked line fulfils the requirement.
        if (!hasOwnAnswer && OfferLineFor(offer, criterion) is not null)
        {
            return criterion.Assess(true, null, null, false);
        }

        if (assessment is null || assessment.RevisionNumber != currentRevisionNumber)
        {
            return Fulfillment.Unknown;
        }

        return criterion.Assess(
            assessment.BooleanValue,
            assessment.MeasurementValue,
            assessment.RatingValue,
            assessment.OptionalAvailable
        );
    }
}

/// <summary>The answer to one offer criterion, valid for the offer version it was given for.</summary>
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
    public bool HasValue =>
        OptionalAvailable
        || BooleanValue.HasValue
        || MeasurementValue.HasValue
        || RatingValue.HasValue;

    private OfferAssessment() { }

    internal OfferAssessment(
        Guid evaluationId,
        Offer offer,
        Criterion criterion,
        AssessmentInput input
    )
    {
        EvaluationId = evaluationId;
        OfferId = offer.Id;
        CriterionId = criterion.Id;
        Update(offer, criterion, input);
    }

    internal void Update(Offer offer, Criterion criterion, AssessmentInput input)
    {
        if (criterion.Scope != CriterionScope.Offer)
        {
            throw new RuleException("OfferCriteriaOnly");
        }

        input.RequireValuesFor(criterion.Type);
        var source = Rules.Defined(input.Source);
        var notes = Rules.OptionalText(input.Notes, Rules.LongNoteLength);

        BooleanValue = input.BooleanValue;
        OptionalAvailable = input.OptionalAvailable;
        MeasurementValue = input.MeasurementValue;
        RatingValue = input.RatingValue;
        Source = source;
        Notes = notes;
        RevisionNumber = offer.Latest.Number;
    }

    /// <summary>Binds the answer to an earlier offer version again, as it was when exported.</summary>
    public void RestoreRevision(Offer offer, int revisionNumber)
    {
        if (offer.Id != OfferId)
        {
            throw new RuleException("InvalidImport");
        }

        _ = offer.Revision(revisionNumber);
        RevisionNumber = revisionNumber;
    }
}
