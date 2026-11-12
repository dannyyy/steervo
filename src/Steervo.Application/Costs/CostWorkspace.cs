using Steervo.Application.Evaluations;
using Steervo.Core;

namespace Steervo.Application.Costs;

public sealed class CostWorkspace(EvaluationWorkspace evaluations)
{
    public Task<Evaluation> ReadAsync(Guid id) => evaluations.ReadAsync(id);
    public Task PlanAsync(Guid id, long version, DateOnly start, int months) => evaluations.EditAsync(id, version, e => e.SetCostPlanning(start, months));
    public async Task<Guid> AddAsync(Guid id, long version, CostInput input)
    {
        var costId = Guid.Empty;
        await evaluations.EditAsync(id, version, e => costId = e.AddCost(input));
        return costId;
    }
    public Task EditAsync(Guid id, long version, Guid costId, CostInput input) => evaluations.EditAsync(id, version, e => e.EditCost(costId, input));
    public Task RemoveAsync(Guid id, long version, Guid costId) => evaluations.EditAsync(id, version, e => e.RemoveCost(costId));
}
