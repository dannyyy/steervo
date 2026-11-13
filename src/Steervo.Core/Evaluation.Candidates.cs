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
    public Candidate FindCandidate(Guid id) => Candidates.SingleOrDefault(x => x.Id == id) ?? throw new RuleException("NotFound");
    public void EditCandidate(Guid id, CandidateInput input) => FindCandidate(id).Update(input);
    public void ClassifyCandidate(Guid id, CandidateStatus status, string? reason) => FindCandidate(id).Classify(status, reason);
    public void MoveCandidate(Guid id, int direction) => Move(Candidates.OrderBy(x => x.Position).ToList(), FindCandidate(id), direction, (x, i) => x.Position = i);
    public Guid CopyCandidate(Guid id, string model)
    {
        var original = FindCandidate(id);
        var costs = Costs.Where(c => c.CandidateId == id).ToArray();
        if (Costs.Count + costs.Length > 1000) throw new RuleException("TooManyCosts");
        var copyId = AddCandidate(original.CopyInput(model));
        foreach (var assessment in Assessments.Where(x => x.CandidateId == id).ToList())
            AssessCandidate(copyId, assessment.CriterionId, assessment.CopyInput());
        foreach (var cost in costs) AddCost(cost.ToInput() with { CandidateId = copyId });
        return copyId;
    }
    public void RemoveCandidate(Guid id)
    {
        var offerIds = Offers.Where(offer => offer.CandidateId == id).Select(offer => offer.Id).ToHashSet();
        Scenarios.RemoveAll(scenario => offerIds.Contains(scenario.OfferId));
        Documents.RemoveAll(document => offerIds.Contains(document.OfferId));
        OfferAssessments.RemoveAll(assessment => offerIds.Contains(assessment.OfferId));
        Offers.RemoveAll(offer => offer.CandidateId == id);
        Candidates.Remove(FindCandidate(id));
        Costs.RemoveAll(cost => cost.CandidateId == id);
        Assessments.RemoveAll(x => x.CandidateId == id);
        var position = 0;
        foreach (var candidate in Candidates.OrderBy(x => x.Position)) candidate.Position = position++;
    }
    public void AssessCandidate(Guid candidateId, Guid criterionId, AssessmentInput input)
    {
        _ = FindCandidate(candidateId);
        var criterion = FindCriterion(criterionId);
        var assessment = Assessments.SingleOrDefault(x => x.CandidateId == candidateId && x.CriterionId == criterionId);
        if (assessment is null) Assessments.Add(new Assessment(Id, candidateId, criterion, input));
        else assessment.Update(criterion, input);
    }
    public void ClearAssessment(Guid candidateId, Guid criterionId)
    {
        _ = FindCandidate(candidateId);
        _ = FindCriterion(criterionId);
        Assessments.RemoveAll(x => x.CandidateId == candidateId && x.CriterionId == criterionId);
    }
    public Fulfillment ResultFor(Guid candidateId, Guid criterionId)
    {
        _ = FindCandidate(candidateId);
        var criterion = FindCriterion(criterionId);
        var assessment = Assessments.SingleOrDefault(x => x.CandidateId == candidateId && x.CriterionId == criterionId);
        return criterion.Assess(assessment?.BooleanValue, assessment?.MeasurementValue, assessment?.RatingValue, assessment?.OptionalAvailable ?? false);
    }
}
