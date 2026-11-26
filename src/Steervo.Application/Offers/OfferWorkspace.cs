using Steervo.Application.Evaluations;
using Steervo.Core;

namespace Steervo.Application.Offers;

/// <summary>Use cases for the offers of an evaluation, their versions and assessments.</summary>
public sealed class OfferWorkspace(EvaluationWorkspace evaluations)
{
    private const int MaximumDocumentNameLength = 160;

    public Task<Evaluation> ReadAsync(Guid id) => evaluations.ReadAsync(id);

    /// <summary>
    /// Adds an offer and, if given, its uploaded file as the offer's first document.
    /// </summary>
    public async Task<Guid> AddAsync(
        Guid evaluationId,
        long version,
        Guid candidateId,
        OfferTerms terms,
        IEnumerable<OfferLineInput> lines,
        string? currency,
        string? changeNote,
        DocumentFile? attachment = null
    )
    {
        var offerId = Guid.Empty;

        await evaluations.EditAsync(evaluationId, version, evaluation =>
        {
            if (evaluation.Currency is null)
            {
                throw new RuleException("CostCurrencyRequired");
            }

            offerId = evaluation.AddOffer(candidateId, terms, lines, currency, changeNote);

            if (attachment is not null)
            {
                evaluation.AddDocument(offerId, AttachmentName(attachment), attachment, null);
            }
        });

        return offerId;
    }

    public Task ReviseAsync(
        Guid evaluationId,
        long version,
        Guid offerId,
        OfferTerms terms,
        IEnumerable<OfferLineInput> lines,
        string? changeNote,
        DocumentFile? attachment = null
    )
    {
        return evaluations.EditAsync(evaluationId, version, evaluation =>
        {
            evaluation.ReviseOffer(offerId, terms, lines, changeNote);

            if (attachment is not null)
            {
                evaluation.AddDocument(offerId, AttachmentName(attachment), attachment, null);
            }
        });
    }

    public Task MoveAsync(
        Guid evaluationId,
        long version,
        Guid offerId,
        int direction,
        Guid? candidateId
    )
    {
        return evaluations.EditAsync(evaluationId, version, evaluation =>
            evaluation.MoveOffer(offerId, direction, candidateId)
        );
    }

    public Task RemoveAsync(Guid evaluationId, long version, Guid offerId)
    {
        return evaluations.EditAsync(evaluationId, version, evaluation =>
            evaluation.RemoveOffer(offerId)
        );
    }

    public Task AssessAsync(
        Guid evaluationId,
        long version,
        Guid offerId,
        Guid criterionId,
        AssessmentInput input
    )
    {
        return evaluations.EditAsync(evaluationId, version, evaluation =>
            evaluation.AssessOffer(offerId, criterionId, input)
        );
    }

    public Task ClearAssessmentAsync(
        Guid evaluationId,
        long version,
        Guid offerId,
        Guid criterionId
    )
    {
        return evaluations.EditAsync(evaluationId, version, evaluation =>
            evaluation.ClearOfferAssessment(offerId, criterionId)
        );
    }

    public Task RemoveDocumentAsync(Guid evaluationId, long version, Guid offerId, Guid documentId)
    {
        return evaluations.EditAsync(evaluationId, version, evaluation =>
        {
            // A document id belonging to another offer is treated like an unknown one.
            if (evaluation.FindDocument(documentId).OfferId != offerId)
            {
                throw new RuleException("NotFound");
            }

            evaluation.RemoveDocument(documentId);
        });
    }

    // The document is named after the uploaded file, shortened to the allowed name length.
    private static string AttachmentName(DocumentFile file)
    {
        var length = Math.Min(file.FileName.Length, MaximumDocumentNameLength);

        return file.FileName[..length];
    }
}
