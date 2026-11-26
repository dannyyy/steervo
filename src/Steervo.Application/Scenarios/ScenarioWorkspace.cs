using Steervo.Application.Evaluations;
using Steervo.Core;

namespace Steervo.Application.Scenarios;

/// <summary>
/// Use cases for the scenarios of an evaluation: calculation settings, the scenarios themselves,
/// their residual values and their cost adjustments.
/// </summary>
public sealed class ScenarioWorkspace(EvaluationWorkspace evaluations)
{
    public Task<Evaluation> ReadAsync(Guid id) => evaluations.ReadAsync(id);

    public Task SettingsAsync(Guid id, long version, decimal annualReturn)
    {
        return evaluations.EditAsync(id, version, evaluation =>
            evaluation.SetCalculationSettings(annualReturn)
        );
    }

    public async Task<Guid> AddAsync(Guid id, long version, ScenarioInput input)
    {
        var scenarioId = Guid.Empty;

        await evaluations.EditAsync(id, version, evaluation =>
            scenarioId = evaluation.AddScenario(input)
        );

        return scenarioId;
    }

    public Task EditAsync(Guid id, long version, Guid scenarioId, ScenarioInput input)
    {
        return evaluations.EditAsync(id, version, evaluation =>
            evaluation.EditScenario(scenarioId, input)
        );
    }

    public Task RemoveAsync(Guid id, long version, Guid scenarioId)
    {
        return evaluations.EditAsync(id, version, evaluation =>
            evaluation.RemoveScenario(scenarioId)
        );
    }

    public async Task<Guid> CopyAsync(Guid id, long version, Guid scenarioId, string name)
    {
        var copyId = Guid.Empty;

        await evaluations.EditAsync(id, version, evaluation =>
            copyId = evaluation.CopyScenario(scenarioId, name)
        );

        return copyId;
    }

    public Task ResidualAsync(Guid id, long version, Guid scenarioId, ResidualInput input)
    {
        return evaluations.EditAsync(id, version, evaluation =>
            evaluation.SetResidual(scenarioId, input)
        );
    }

    public Task RemoveResidualAsync(Guid id, long version, Guid scenarioId, int month)
    {
        return evaluations.EditAsync(id, version, evaluation =>
            evaluation.RemoveResidual(scenarioId, month)
        );
    }

    public Task AdjustmentAsync(Guid id, long version, Guid scenarioId, CostAdjustmentInput input)
    {
        return evaluations.EditAsync(id, version, evaluation =>
            evaluation.SetCostAdjustment(scenarioId, input)
        );
    }

    public Task RemoveAdjustmentAsync(Guid id, long version, Guid scenarioId, Guid costId)
    {
        return evaluations.EditAsync(id, version, evaluation =>
            evaluation.RemoveCostAdjustment(scenarioId, costId)
        );
    }
}
