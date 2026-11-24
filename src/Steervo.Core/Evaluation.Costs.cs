namespace Steervo.Core;

public sealed partial class Evaluation
{
    private const int MaximumCosts = 1000;

    public CostPlanning? CostPlanning { get; private set; }
    public List<CostItem> Costs { get; private set; } = [];

    public void SetCostPlanning(DateOnly start, int months)
    {
        if (CostPlanning is null)
        {
            CostPlanning = new(Id, start, months);
        }
        else
        {
            CostPlanning.Update(start, months);
        }
    }

    public CostItem FindCost(Guid id) =>
        Costs.SingleOrDefault(cost => cost.Id == id) ?? throw new RuleException("NotFound");

    public Guid AddCost(CostInput input)
    {
        RequireCostPlanning(input);

        if (Costs.Count >= MaximumCosts)
        {
            throw new RuleException("TooManyCosts");
        }

        var item = new CostItem(Id, input);
        Costs.Add(item);

        return item.Id;
    }

    public void EditCost(Guid id, CostInput input)
    {
        RequireCostPlanning(input);

        if (Scenarios.Any(scenario => WouldContradictAdjustment(scenario, id, input)))
        {
            throw new RuleException("CostUsedByScenario");
        }

        FindCost(id).Update(input);
    }

    public void RemoveCost(Guid id)
    {
        Costs.Remove(FindCost(id));

        foreach (var scenario in Scenarios)
        {
            scenario.CostAdjustments.RemoveAll(adjustment => adjustment.CostId == id);
        }
    }

    private void RequireCostPlanning(CostInput input)
    {
        if (CostPlanning is null)
        {
            throw new RuleException("CostPlanningRequired");
        }

        if (Currency is null)
        {
            throw new RuleException("CostCurrencyRequired");
        }

        if (input.CandidateId.HasValue)
        {
            _ = FindCandidate(input.CandidateId.Value);
        }
    }

    // A scenario may adjust a cost. The edited cost must still apply to that scenario's car, and
    // a cost that becomes "included" cannot keep an adjustment that puts a price on it.
    private bool WouldContradictAdjustment(Scenario scenario, Guid costId, CostInput input)
    {
        var adjustsCost = scenario.CostAdjustments.Any(adjustment => adjustment.CostId == costId);

        if (!adjustsCost)
        {
            return false;
        }

        var movesToAnotherCandidate =
            input.CandidateId.HasValue
            && FindOffer(scenario.OfferId).CandidateId != input.CandidateId;

        if (movesToAnotherCandidate)
        {
            return true;
        }

        var hasPricedAdjustment = scenario.CostAdjustments.Any(adjustment =>
            adjustment.CostId == costId && adjustment.Amount is not (null or 0)
        );

        return input.Frequency == CostFrequency.Included && hasPricedAdjustment;
    }
}
