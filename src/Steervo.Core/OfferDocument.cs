namespace Steervo.Core;

public sealed record DocumentFile(Guid ContentId, string FileName, long Length, string Sha256)
{
    public const long MaximumBytes = 25 * 1024 * 1024;
    public DocumentFile Validated()
    {
        if (ContentId == Guid.Empty || Length is < 1 or > MaximumBytes || Sha256 is null || Sha256.Length != 64
            || Sha256.Any(c => !char.IsAsciiHexDigit(c))) throw new RuleException("InvalidDocument");
        var name = Rules.Text(FileName, 200);
        if (name is "." or ".." || name.Any(c => char.IsControl(c) || c is '/' or '\\')) throw new RuleException("InvalidDocument");
        return this with { FileName = name, Sha256 = Sha256.ToLowerInvariant() };
    }
}

public sealed class OfferDocument
{
    private readonly List<DocumentRevision> revisions = [];
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EvaluationId { get; private set; }
    public Guid OfferId { get; private set; }
    public string Name { get; private set; } = "";
    public IReadOnlyList<DocumentRevision> Revisions => revisions.AsReadOnly();
    public DocumentRevision Latest => revisions.MaxBy(r => r.Number) ?? throw new RuleException("NotFound");
    private OfferDocument() { }
    internal OfferDocument(Guid evaluationId, Guid offerId, string name)
    {
        EvaluationId = evaluationId; OfferId = offerId; Name = Rules.Text(name, 160);
    }
    internal void Append(int offerRevision, DocumentFile file, string? note, DateTimeOffset? createdAt = null)
    {
        var revision = new DocumentRevision(EvaluationId, OfferId, Id, revisions.Count + 1, offerRevision, file, note, createdAt);
        if (revisions.Count >= 100 || (revisions.Count > 0 && (revision.CreatedAt < Latest.CreatedAt || offerRevision < Latest.OfferRevisionNumber)))
            throw new RuleException("InvalidDocument");
        revisions.Add(revision);
    }
}

public sealed class DocumentRevision
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EvaluationId { get; private set; }
    public Guid OfferId { get; private set; }
    public Guid DocumentId { get; private set; }
    public int Number { get; private set; }
    public int OfferRevisionNumber { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string Note { get; private set; } = "";
    public DocumentFile File { get; private set; } = new(Guid.Empty, "", 0, "");
    private DocumentRevision() { }
    internal DocumentRevision(Guid evaluationId, Guid offerId, Guid documentId, int number, int offerRevision, DocumentFile file, string? note, DateTimeOffset? createdAt)
    {
        EvaluationId = evaluationId; OfferId = offerId; DocumentId = documentId; Number = number; OfferRevisionNumber = offerRevision;
        File = file.Validated(); Note = Rules.OptionalText(note, 2000); CreatedAt = (createdAt ?? DateTimeOffset.UtcNow).ToUniversalTime();
        if (CreatedAt.Year < 1900 || CreatedAt > DateTimeOffset.UtcNow.AddMinutes(5)) throw new RuleException("InvalidDocument");
    }
}
