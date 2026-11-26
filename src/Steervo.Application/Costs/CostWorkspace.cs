using Steervo.Application.Evaluations;
using Steervo.Core;

namespace Steervo.Application.Costs;

/// <summary>
/// Use cases for the cost planning period and the planned costs of an evaluation.
/// </summary>
public sealed class CostWorkspace(EvaluationWorkspace evaluations)
{
    public Task<Evaluation> ReadAsync(Guid id) => evaluations.ReadAsync(id);

    public Task PlanAsync(Guid id, long version, DateOnly start, int months)
    {
        return evaluations.EditAsync(id, version, evaluation =>
            evaluation.SetCostPlanning(start, months)
        );
    }

    public async Task<Guid> AddAsync(Guid id, long version, CostInput input)
    {
        var costId = Guid.Empty;

        await evaluations.EditAsync(id, version, evaluation => costId = evaluation.AddCost(input));

        return costId;
    }

    public Task EditAsync(Guid id, long version, Guid costId, CostInput input)
    {
        return evaluations.EditAsync(id, version, evaluation => evaluation.EditCost(costId, input));
    }

    public Task RemoveAsync(Guid id, long version, Guid costId)
    {
        return evaluations.EditAsync(id, version, evaluation => evaluation.RemoveCost(costId));
    }
}
