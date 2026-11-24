using Steervo.Application.Evaluations;
using Steervo.Core;

namespace Steervo.Application.Documents;

public interface IDocumentStorage
{
    Task<DocumentFile> SaveAsync(
        Stream input,
        string fileName,
        CancellationToken cancellationToken = default
    );

    Task<Stream> OpenAsync(DocumentFile file, CancellationToken cancellationToken = default);
}

/// <summary>Use cases for the documents attached to the offers of an evaluation.</summary>
public sealed class DocumentWorkspace(
    EvaluationWorkspace evaluations,
    ICurrentUser current,
    IDocumentStorage storage
)
{
    private const int MaximumNameLength = 160;
    private const int MaximumNoteLength = 2000;

    /// <summary>
    /// Stores an uploaded file as a new document of the offer or, when a document id is given,
    /// as a new version of that document.
    /// </summary>
    public async Task UploadAsync(
        Guid evaluationId,
        long version,
        Guid offerId,
        Guid? documentId,
        string name,
        Stream input,
        string fileName,
        string? note,
        CancellationToken cancellationToken = default
    )
    {
        // Everything that can be rejected is checked before the file is written to storage.
        var evaluation = await evaluations.ReadAsync(evaluationId);
        evaluation.Require(current.Id, AccessLevel.Editor);
        _ = evaluation.FindOffer(offerId);

        if (evaluation.Version != version)
        {
            throw new RuleException("Conflict");
        }

        if (documentId.HasValue && evaluation.FindDocument(documentId.Value).OfferId != offerId)
        {
            throw new RuleException("NotFound");
        }

        var documentName = documentId.HasValue
            ? evaluation.FindDocument(documentId.Value).Name
            : name;
        _ = Rules.Text(documentName, MaximumNameLength);
        _ = Rules.OptionalText(note, MaximumNoteLength);

        var file = await storage.SaveAsync(input, fileName, cancellationToken);

        await evaluations.EditAsync(
            evaluationId,
            version,
            edited =>
            {
                if (documentId.HasValue)
                {
                    edited.ReviseDocument(documentId.Value, file, note);
                }
                else
                {
                    edited.AddDocument(offerId, name, file, note);
                }
            }
        );
    }

    public async Task<(Stream Content, string FileName)> DownloadAsync(
        Guid evaluationId,
        Guid documentId,
        int number,
        CancellationToken cancellationToken = default
    )
    {
        var evaluation = await evaluations.ReadAsync(evaluationId);
        var revision =
            evaluation.FindDocument(documentId).Revisions.SingleOrDefault(r => r.Number == number)
            ?? throw new RuleException("NotFound");

        var content = await storage.OpenAsync(revision.File, cancellationToken);

        return (content, revision.File.FileName);
    }

    public Task RemoveAsync(Guid evaluationId, long version, Guid documentId)
    {
        return evaluations.EditAsync(
            evaluationId,
            version,
            evaluation => evaluation.RemoveDocument(documentId)
        );
    }
}
