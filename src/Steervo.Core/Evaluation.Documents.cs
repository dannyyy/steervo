namespace Steervo.Core;

public sealed partial class Evaluation
{
    public const long MaximumDocumentBytes = 200 * 1024 * 1024;
    public const int MaximumDocumentRevisions = 1000;
    private const int MaximumDocuments = 1000;

    public List<OfferDocument> Documents { get; private set; } = [];

    public OfferDocument FindDocument(Guid id) =>
        Documents.SingleOrDefault(document => document.Id == id)
        ?? throw new RuleException("NotFound");

    /// <summary>Attaches a new document to the current version of an offer.</summary>
    public Guid AddDocument(Guid offerId, string name, DocumentFile file, string? note = null)
    {
        var offer = FindOffer(offerId);

        return RestoreDocument(
            offerId,
            name,
            [(offer.Latest.Number, file, note, DateTimeOffset.UtcNow)]
        );
    }

    public void ReviseDocument(Guid documentId, DocumentFile file, string? note = null)
    {
        var document = FindDocument(documentId);
        var offer = FindOffer(document.OfferId);

        // The file is validated first: the capacity check adds up lengths it can rely on.
        var validFile = file.Validated();

        CheckDocumentCapacity([validFile]);
        document.Append(offer.Latest.Number, validFile, note);
    }

    /// <summary>
    /// Creates a document with a given history of versions, each belonging to an offer version
    /// and carrying its original date.
    /// </summary>
    public Guid RestoreDocument(
        Guid offerId,
        string name,
        IEnumerable<(
            int OfferRevision,
            DocumentFile File,
            string? Note,
            DateTimeOffset CreatedAt
        )> versions
    )
    {
        var offer = FindOffer(offerId);

        if (Documents.Count >= MaximumDocuments)
        {
            throw new RuleException("InvalidDocument");
        }

        var document = new OfferDocument(Id, offerId, name);

        foreach (var version in versions)
        {
            // A document version cannot be older than the offer version it belongs to.
            if (version.CreatedAt < offer.Revision(version.OfferRevision).CreatedAt)
            {
                throw new RuleException("InvalidDocument");
            }

            document.Append(version.OfferRevision, version.File, version.Note, version.CreatedAt);
        }

        // A document without any version is rejected: reading the latest one fails.
        _ = document.Latest;

        CheckDocumentCapacity(document.Revisions.Select(revision => revision.File));
        Documents.Add(document);

        return document.Id;
    }

    public void RemoveDocument(Guid id)
    {
        var document = FindDocument(id);
        Documents.Remove(document);
    }

    private void CheckDocumentCapacity(IEnumerable<DocumentFile> additions)
    {
        var files = Documents
            .SelectMany(document => document.Revisions)
            .Select(revision => revision.File)
            .Concat(additions)
            .ToArray();

        if (files.Length > MaximumDocumentRevisions)
        {
            throw new RuleException("DocumentCapacityReached");
        }

        if (files.Sum(file => file.Length) > MaximumDocumentBytes)
        {
            throw new RuleException("DocumentCapacityReached");
        }
    }
}
