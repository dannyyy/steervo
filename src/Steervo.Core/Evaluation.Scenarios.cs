namespace Steervo.Core;

public sealed partial class Evaluation
{
    private const int MaximumScenarios = 200;

    public CalculationSettings? CalculationSettings { get; private set; }
    public List<Scenario> Scenarios { get; private set; } = [];

    public void SetCalculationSettings(decimal annualReturnPercent)
    {
        if (CalculationSettings is null)
        {
            CalculationSettings = new(Id, annualReturnPercent);
        }
        else
        {
            CalculationSettings.Update(annualReturnPercent);
        }
    }

    public Scenario FindScenario(Guid id) =>
        Scenarios.SingleOrDefault(scenario => scenario.Id == id)
        ?? throw new RuleException("NotFound");

    public Guid AddScenario(ScenarioInput input)
    {
        ValidateScenario(input);

        if (Scenarios.Count >= MaximumScenarios)
        {
            throw new RuleException("TooManyScenarios");
        }

        var scenario = new Scenario(Id, input);
        Scenarios.Add(scenario);

        return scenario.Id;
    }

    public void EditScenario(Guid id, ScenarioInput input)
    {
        ValidateScenario(input);
        var scenario = FindScenario(id);

        // A scenario may follow its offer to another version, but never switch to another offer.
        if (scenario.OfferId != input.OfferId)
        {
            throw new RuleException("ScenarioOfferFixed");
        }

        scenario.Update(input);
    }

    public void RemoveScenario(Guid id) => Scenarios.Remove(FindScenario(id));

    public Guid CopyScenario(Guid id, string name)
    {
        var original = FindScenario(id);
        var copyId = AddScenario(original.ToInput() with { Name = name });
        var copy = FindScenario(copyId);

        foreach (var residual in original.Residuals)
        {
            copy.SetResidual(residual.ToInput());
        }

        foreach (var adjustment in original.CostAdjustments)
        {
            copy.SetAdjustment(adjustment.ToInput());
        }

        return copyId;
    }

    public void SetResidual(Guid scenarioId, ResidualInput input) =>
        FindScenario(scenarioId).SetResidual(input);

    public void RemoveResidual(Guid scenarioId, int month) =>
        FindScenario(scenarioId).Residuals.RemoveAll(residual => residual.Month == month);

    /// <summary>Overrides the amount or period of one cost within a single scenario.</summary>
    public void SetCostAdjustment(Guid scenarioId, CostAdjustmentInput input)
    {
        var scenario = FindScenario(scenarioId);
        var cost = FindCost(input.CostId);

        var belongsToAnotherCandidate =
            cost.CandidateId.HasValue
            && cost.CandidateId != FindOffer(scenario.OfferId).CandidateId;
        var pricesIncludedCost =
            cost.Frequency == CostFrequency.Included && input.Amount is not (null or 0);

        if (belongsToAnotherCandidate || pricesIncludedCost)
        {
            throw new RuleException("InvalidCostAdjustment");
        }

        scenario.SetAdjustment(input);
    }

    public void RemoveCostAdjustment(Guid scenarioId, Guid costId) =>
        FindScenario(scenarioId)
            .CostAdjustments.RemoveAll(adjustment => adjustment.CostId == costId);

    private void ValidateScenario(ScenarioInput input)
    {
        if (CostPlanning is null || Currency is null)
        {
            throw new RuleException("ScenarioPlanningRequired");
        }

        _ = FindOffer(input.OfferId).Revision(input.RevisionNumber);
    }
}
