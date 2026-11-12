using Steervo.Core;

namespace Steervo.Application.Evaluations;

public sealed class CandidateWorkspace(EvaluationWorkspace evaluations)
{
    public Task<Evaluation> ReadAsync(Guid evaluationId) => evaluations.ReadAsync(evaluationId);
    public async Task<Guid> AddAsync(Guid evaluationId, long version, CandidateInput input)
    {
        var candidateId = Guid.Empty;
        await evaluations.EditAsync(evaluationId, version, e => candidateId = e.AddCandidate(input));
        return candidateId;
    }
    public Task EditAsync(Guid evaluationId, long version, Guid candidateId, CandidateInput input) =>
        evaluations.EditAsync(evaluationId, version, e => e.EditCandidate(candidateId, input));
    public Task ClassifyAsync(Guid evaluationId, long version, Guid candidateId, CandidateStatus status, string? reason) =>
        evaluations.EditAsync(evaluationId, version, e => e.ClassifyCandidate(candidateId, status, reason));
    public Task MoveAsync(Guid evaluationId, long version, Guid candidateId, int direction) =>
        evaluations.EditAsync(evaluationId, version, e => e.MoveCandidate(candidateId, direction));
    public async Task<Guid> CopyAsync(Guid evaluationId, long version, Guid candidateId, string model)
    {
        var copyId = Guid.Empty;
        await evaluations.EditAsync(evaluationId, version, e => copyId = e.CopyCandidate(candidateId, model));
        return copyId;
    }
    public Task RemoveAsync(Guid evaluationId, long version, Guid candidateId) =>
        evaluations.EditAsync(evaluationId, version, e => e.RemoveCandidate(candidateId));
    public Task AssessAsync(Guid evaluationId, long version, Guid candidateId, Guid criterionId, AssessmentInput input) =>
        evaluations.EditAsync(evaluationId, version, e => e.AssessCandidate(candidateId, criterionId, input));
    public Task ClearAssessmentAsync(Guid evaluationId, long version, Guid candidateId, Guid criterionId) =>
        evaluations.EditAsync(evaluationId, version, e => e.ClearAssessment(candidateId, criterionId));
}
