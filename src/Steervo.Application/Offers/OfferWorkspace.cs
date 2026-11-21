using Steervo.Application.Evaluations;
using Steervo.Core;

namespace Steervo.Application.Offers;

public sealed class OfferWorkspace(EvaluationWorkspace evaluations)
{
    public Task<Evaluation> ReadAsync(Guid id) => evaluations.ReadAsync(id);
    public async Task<Guid> AddAsync(Guid evaluationId, long version, Guid candidateId, OfferTerms terms,
        IEnumerable<OfferLineInput> lines, string? currency, string? changeNote, DocumentFile? attachment = null)
    {
        var id = Guid.Empty;
        await evaluations.EditAsync(evaluationId, version, evaluation => {
            if (evaluation.Currency is null) throw new RuleException("CostCurrencyRequired");
            id = evaluation.AddOffer(candidateId, terms, lines, currency, changeNote);
            if (attachment is not null) evaluation.AddDocument(id, AttachmentName(attachment), attachment, null);
        });
        return id;
    }
    public Task ReviseAsync(Guid evaluationId, long version, Guid offerId, OfferTerms terms, IEnumerable<OfferLineInput> lines, string? changeNote, DocumentFile? attachment = null) =>
        evaluations.EditAsync(evaluationId, version, evaluation => {
            evaluation.ReviseOffer(offerId, terms, lines, changeNote);
            if (attachment is not null) evaluation.AddDocument(offerId, AttachmentName(attachment), attachment, null);
        });
    private static string AttachmentName(DocumentFile file) => file.FileName[..Math.Min(file.FileName.Length, 160)];
    public Task MoveAsync(Guid evaluationId, long version, Guid offerId, int direction, Guid? candidateId) =>
        evaluations.EditAsync(evaluationId, version, evaluation => evaluation.MoveOffer(offerId, direction, candidateId));
    public Task RemoveAsync(Guid evaluationId, long version, Guid offerId) =>
        evaluations.EditAsync(evaluationId, version, evaluation => evaluation.RemoveOffer(offerId));
    public Task AssessAsync(Guid evaluationId, long version, Guid offerId, Guid criterionId, AssessmentInput input) =>
        evaluations.EditAsync(evaluationId, version, evaluation => evaluation.AssessOffer(offerId, criterionId, input));
    public Task ClearAssessmentAsync(Guid evaluationId, long version, Guid offerId, Guid criterionId) =>
        evaluations.EditAsync(evaluationId, version, evaluation => evaluation.ClearOfferAssessment(offerId, criterionId));
    public Task RemoveDocumentAsync(Guid evaluationId, long version, Guid offerId, Guid documentId) =>
        evaluations.EditAsync(evaluationId, version, evaluation => {
            if (evaluation.FindDocument(documentId).OfferId != offerId) throw new RuleException("NotFound");
            evaluation.RemoveDocument(documentId);
        });
}
