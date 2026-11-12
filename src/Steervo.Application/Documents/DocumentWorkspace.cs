using Steervo.Application.Evaluations;
using Steervo.Core;

namespace Steervo.Application.Documents;

public interface IDocumentStorage
{
    Task<DocumentFile> SaveAsync(Stream input, string fileName, CancellationToken cancellationToken = default);
    Task<Stream> OpenAsync(DocumentFile file, CancellationToken cancellationToken = default);
}

public sealed class DocumentWorkspace(EvaluationWorkspace evaluations, ICurrentUser current, IDocumentStorage storage)
{
    public async Task UploadAsync(Guid evaluationId, long version, Guid offerId, Guid? documentId, string name, Stream input, string fileName, string? note, CancellationToken cancellationToken = default)
    {
        var evaluation = await evaluations.ReadAsync(evaluationId);
        evaluation.Require(current.Id, AccessLevel.Editor);
        if (evaluation.FindOffer(offerId).Archived) throw new RuleException("OfferArchived");
        if (evaluation.Version != version) throw new RuleException("Conflict");
        if (documentId.HasValue && evaluation.FindDocument(documentId.Value).OfferId != offerId) throw new RuleException("NotFound");
        _ = Rules.Text(documentId.HasValue ? evaluation.FindDocument(documentId.Value).Name : name, 160);
        _ = Rules.OptionalText(note, 2000);
        var file = await storage.SaveAsync(input, fileName, cancellationToken);
        await evaluations.EditAsync(evaluationId, version, e => {
            if (documentId.HasValue) e.ReviseDocument(documentId.Value, file, note);
            else e.AddDocument(offerId, name, file, note);
        });
    }
    public async Task<(Stream Content, string FileName)> DownloadAsync(Guid evaluationId, Guid documentId, int number, CancellationToken cancellationToken = default)
    {
        var evaluation = await evaluations.ReadAsync(evaluationId);
        var revision = evaluation.FindDocument(documentId).Revisions.SingleOrDefault(r => r.Number == number) ?? throw new RuleException("NotFound");
        return (await storage.OpenAsync(revision.File, cancellationToken), revision.File.FileName);
    }
    public Task RemoveAsync(Guid evaluationId, long version, Guid documentId) =>
        evaluations.EditAsync(evaluationId, version, e => e.RemoveDocument(documentId));
}
