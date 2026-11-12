using Steervo.Application.Evaluations;
using Steervo.Core;

namespace Steervo.Application.Scenarios;

public sealed class ScenarioWorkspace(EvaluationWorkspace evaluations)
{
    public Task<Evaluation> ReadAsync(Guid id) => evaluations.ReadAsync(id);
    public Task SettingsAsync(Guid id, long version, decimal annualReturn) => evaluations.EditAsync(id, version, e => e.SetCalculationSettings(annualReturn));
    public async Task<Guid> AddAsync(Guid id, long version, ScenarioInput input)
    {
        var scenarioId = Guid.Empty;
        await evaluations.EditAsync(id, version, e => scenarioId = e.AddScenario(input)); return scenarioId;
    }
    public Task EditAsync(Guid id, long version, Guid scenarioId, ScenarioInput input) => evaluations.EditAsync(id, version, e => e.EditScenario(scenarioId, input));
    public Task RemoveAsync(Guid id, long version, Guid scenarioId) => evaluations.EditAsync(id, version, e => e.RemoveScenario(scenarioId));
    public async Task<Guid> CopyAsync(Guid id, long version, Guid scenarioId, string name)
    {
        var copyId = Guid.Empty;
        await evaluations.EditAsync(id, version, e => copyId = e.CopyScenario(scenarioId, name)); return copyId;
    }
    public Task ResidualAsync(Guid id, long version, Guid scenarioId, ResidualInput input) => evaluations.EditAsync(id, version, e => e.SetResidual(scenarioId, input));
    public Task RemoveResidualAsync(Guid id, long version, Guid scenarioId, int month) => evaluations.EditAsync(id, version, e => e.RemoveResidual(scenarioId, month));
    public Task AdjustmentAsync(Guid id, long version, Guid scenarioId, CostAdjustmentInput input) => evaluations.EditAsync(id, version, e => e.SetCostAdjustment(scenarioId, input));
    public Task RemoveAdjustmentAsync(Guid id, long version, Guid scenarioId, Guid costId) => evaluations.EditAsync(id, version, e => e.RemoveCostAdjustment(scenarioId, costId));
}
