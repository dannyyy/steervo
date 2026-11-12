namespace Steervo.Core;

public sealed partial class Evaluation
{
    public const long MaximumDocumentBytes = 200 * 1024 * 1024;
    public const int MaximumDocumentRevisions = 1000;
    public List<OfferDocument> Documents { get; private set; } = [];
    public OfferDocument FindDocument(Guid id) => Documents.SingleOrDefault(d => d.Id == id) ?? throw new RuleException("NotFound");
    public Guid AddDocument(Guid offerId, string name, DocumentFile file, string? note = null)
    {
        var offer = FindOffer(offerId);
        if (offer.Archived) throw new RuleException("OfferArchived");
        return RestoreDocument(offerId, name, [(offer.Latest.Number, file, note, DateTimeOffset.UtcNow)]);
    }
    public void ReviseDocument(Guid documentId, DocumentFile file, string? note = null)
    {
        var document = FindDocument(documentId); var offer = FindOffer(document.OfferId);
        if (offer.Archived) throw new RuleException("OfferArchived");
        CheckDocumentCapacity([file]);
        document.Append(offer.Latest.Number, file, note);
    }
    public Guid RestoreDocument(Guid offerId, string name, IEnumerable<(int OfferRevision, DocumentFile File, string? Note, DateTimeOffset CreatedAt)> versions)
    {
        var offer = FindOffer(offerId);
        if (Documents.Count >= 1000) throw new RuleException("InvalidDocument");
        var document = new OfferDocument(Id, offerId, name);
        foreach (var version in versions)
        {
            if (version.CreatedAt < offer.Revision(version.OfferRevision).CreatedAt) throw new RuleException("InvalidDocument");
            document.Append(version.OfferRevision, version.File, version.Note, version.CreatedAt);
        }
        _ = document.Latest;
        CheckDocumentCapacity(document.Revisions.Select(r => r.File));
        Documents.Add(document); return document.Id;
    }
    public void RemoveDocument(Guid id)
    {
        var document = FindDocument(id);
        if (FindOffer(document.OfferId).Archived) throw new RuleException("OfferArchived");
        Documents.Remove(document);
    }
    private void CheckDocumentCapacity(IEnumerable<DocumentFile> additions)
    {
        var files = Documents.SelectMany(d => d.Revisions).Select(r => r.File).Concat(additions).ToArray();
        if (files.Length > MaximumDocumentRevisions || files.Sum(f => f.Length) > MaximumDocumentBytes) throw new RuleException("DocumentCapacityReached");
    }
}
