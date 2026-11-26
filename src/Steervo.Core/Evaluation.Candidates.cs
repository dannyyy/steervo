namespace Steervo.Core;

public sealed partial class Evaluation
{
    public List<Candidate> Candidates { get; private set; } = [];
    public List<Assessment> Assessments { get; private set; } = [];

    public Guid AddCandidate(CandidateInput input)
    {
        var candidate = new Candidate(Id, input, Candidates.Count);
        Candidates.Add(candidate);

        return candidate.Id;
    }

    public Candidate FindCandidate(Guid id) =>
        Candidates.SingleOrDefault(candidate => candidate.Id == id)
        ?? throw new RuleException("NotFound");

    public void EditCandidate(Guid id, CandidateInput input) => FindCandidate(id).Update(input);

    public void ClassifyCandidate(Guid id, CandidateStatus status, string? reason) =>
        FindCandidate(id).Classify(status, reason);

    public void MoveCandidate(Guid id, int direction)
    {
        var orderedCandidates = Candidates.OrderBy(candidate => candidate.Position).ToList();

        Move(orderedCandidates, FindCandidate(id), direction, (candidate, position) =>
            candidate.Position = position
        );
    }

    /// <summary>
    /// Duplicates a car under a new model name, together with its assessments and its own costs.
    /// Offers are not copied.
    /// </summary>
    public Guid CopyCandidate(Guid id, string model)
    {
        var original = FindCandidate(id);
        var costs = Costs.Where(cost => cost.CandidateId == id).ToArray();

        // Checked up front so that a copy is never left behind with only part of its costs.
        if (Costs.Count + costs.Length > MaximumCosts)
        {
            throw new RuleException("TooManyCosts");
        }

        var copyId = AddCandidate(original.CopyInput(model));
        var assessments = Assessments.Where(assessment => assessment.CandidateId == id).ToList();

        foreach (var assessment in assessments)
        {
            AssessCandidate(copyId, assessment.CriterionId, assessment.CopyInput());
        }

        foreach (var cost in costs)
        {
            AddCost(cost.ToInput() with { CandidateId = copyId });
        }

        return copyId;
    }

    /// <summary>Removes a car together with everything that only exists because of it.</summary>
    public void RemoveCandidate(Guid id)
    {
        var candidate = FindCandidate(id);
        var offerIds = Offers
            .Where(offer => offer.CandidateId == id)
            .Select(offer => offer.Id)
            .ToHashSet();

        Scenarios.RemoveAll(scenario => offerIds.Contains(scenario.OfferId));
        Documents.RemoveAll(document => offerIds.Contains(document.OfferId));
        OfferAssessments.RemoveAll(assessment => offerIds.Contains(assessment.OfferId));
        Offers.RemoveAll(offer => offer.CandidateId == id);
        NormalizeOffers();

        Candidates.Remove(candidate);
        Costs.RemoveAll(cost => cost.CandidateId == id);
        Assessments.RemoveAll(assessment => assessment.CandidateId == id);
        NormalizeCandidates();
    }

    public void AssessCandidate(Guid candidateId, Guid criterionId, AssessmentInput input)
    {
        _ = FindCandidate(candidateId);
        var criterion = FindCriterion(criterionId);
        var assessment = AssessmentFor(candidateId, criterionId);

        if (assessment is null)
        {
            Assessments.Add(new Assessment(Id, candidateId, criterion, input));
        }
        else
        {
            assessment.Update(criterion, input);
        }
    }

    public void ClearAssessment(Guid candidateId, Guid criterionId)
    {
        _ = FindCandidate(candidateId);
        _ = FindCriterion(criterionId);

        Assessments.RemoveAll(assessment =>
            assessment.CandidateId == candidateId && assessment.CriterionId == criterionId
        );
    }

    public Fulfillment ResultFor(Guid candidateId, Guid criterionId)
    {
        _ = FindCandidate(candidateId);
        var criterion = FindCriterion(criterionId);
        var assessment = AssessmentFor(candidateId, criterionId);

        return criterion.Assess(
            assessment?.BooleanValue,
            assessment?.MeasurementValue,
            assessment?.RatingValue,
            assessment?.OptionalAvailable ?? false
        );
    }

    private Assessment? AssessmentFor(Guid candidateId, Guid criterionId) =>
        Assessments.SingleOrDefault(assessment =>
            assessment.CandidateId == candidateId && assessment.CriterionId == criterionId
        );

    private void NormalizeCandidates()
    {
        var position = 0;

        foreach (var candidate in Candidates.OrderBy(candidate => candidate.Position))
        {
            candidate.Position = position++;
        }
    }
}
