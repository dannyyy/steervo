using Steervo.Core;

namespace Steervo.Application.Evaluations;

/// <summary>Use cases for the candidates of an evaluation and their assessments.</summary>
public sealed class CandidateWorkspace(EvaluationWorkspace evaluations)
{
    public Task<Evaluation> ReadAsync(Guid evaluationId) => evaluations.ReadAsync(evaluationId);

    public async Task<Guid> AddAsync(Guid evaluationId, long version, CandidateInput input)
    {
        var candidateId = Guid.Empty;

        await evaluations.EditAsync(evaluationId, version, evaluation =>
            candidateId = evaluation.AddCandidate(input)
        );

        return candidateId;
    }

    public Task EditAsync(Guid evaluationId, long version, Guid candidateId, CandidateInput input)
    {
        return evaluations.EditAsync(evaluationId, version, evaluation =>
            evaluation.EditCandidate(candidateId, input)
        );
    }

    public Task ClassifyAsync(
        Guid evaluationId,
        long version,
        Guid candidateId,
        CandidateStatus status,
        string? reason
    )
    {
        return evaluations.EditAsync(evaluationId, version, evaluation =>
            evaluation.ClassifyCandidate(candidateId, status, reason)
        );
    }

    public Task MoveAsync(Guid evaluationId, long version, Guid candidateId, int direction)
    {
        return evaluations.EditAsync(evaluationId, version, evaluation =>
            evaluation.MoveCandidate(candidateId, direction)
        );
    }

    public async Task<Guid> CopyAsync(
        Guid evaluationId,
        long version,
        Guid candidateId,
        string model
    )
    {
        var copyId = Guid.Empty;

        await evaluations.EditAsync(evaluationId, version, evaluation =>
            copyId = evaluation.CopyCandidate(candidateId, model)
        );

        return copyId;
    }

    public Task RemoveAsync(Guid evaluationId, long version, Guid candidateId)
    {
        return evaluations.EditAsync(evaluationId, version, evaluation =>
            evaluation.RemoveCandidate(candidateId)
        );
    }

    public Task AssessAsync(
        Guid evaluationId,
        long version,
        Guid candidateId,
        Guid criterionId,
        AssessmentInput input
    )
    {
        return evaluations.EditAsync(evaluationId, version, evaluation =>
            evaluation.AssessCandidate(candidateId, criterionId, input)
        );
    }

    public Task ClearAssessmentAsync(
        Guid evaluationId,
        long version,
        Guid candidateId,
        Guid criterionId
    )
    {
        return evaluations.EditAsync(evaluationId, version, evaluation =>
            evaluation.ClearAssessment(candidateId, criterionId)
        );
    }
}
