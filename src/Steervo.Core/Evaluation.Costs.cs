namespace Steervo.Core;

public sealed partial class Evaluation
{
    public CostPlanning? CostPlanning { get; private set; }
    public List<CostItem> Costs { get; private set; } = [];
    public void SetCostPlanning(DateOnly start, int months)
    {
        if (CostPlanning is null) CostPlanning = new(Id, start, months);
        else CostPlanning.Update(start, months);
    }
    public CostItem FindCost(Guid id) => Costs.SingleOrDefault(c => c.Id == id) ?? throw new RuleException("NotFound");
    public Guid AddCost(CostInput input)
    {
        RequireCostPlanning(input);
        if (Costs.Count >= 1000) throw new RuleException("TooManyCosts");
        var item = new CostItem(Id, input); Costs.Add(item); return item.Id;
    }
    public void EditCost(Guid id, CostInput input)
    {
        RequireCostPlanning(input);
        if (Scenarios.Any(s => s.CostAdjustments.Any(a => a.CostId == id)
            && ((input.CandidateId.HasValue && FindOffer(s.OfferId).CandidateId != input.CandidateId)
                || (input.Frequency == CostFrequency.Included && s.CostAdjustments.Any(a => a.CostId == id && a.Amount is not (null or 0))))))
            throw new RuleException("CostUsedByScenario");
        FindCost(id).Update(input);
    }
    public void RemoveCost(Guid id)
    {
        Costs.Remove(FindCost(id));
        foreach (var scenario in Scenarios) scenario.CostAdjustments.RemoveAll(a => a.CostId == id);
    }
    private void RequireCostPlanning(CostInput input)
    {
        if (CostPlanning is null) throw new RuleException("CostPlanningRequired");
        if (Currency is null) throw new RuleException("CostCurrencyRequired");
        if (input.CandidateId.HasValue) _ = FindCandidate(input.CandidateId.Value);
    }
}
