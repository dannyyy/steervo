namespace Steervo.Core;

/// <summary>Describes a stored file: where its content lives, its name, its size and its checksum.</summary>
public sealed record DocumentFile(Guid ContentId, string FileName, long Length, string Sha256)
{
    public const long MaximumBytes = 25 * 1024 * 1024;
    private const int MaximumFileNameLength = 200;
    private const int Sha256HexLength = 64;

    public DocumentFile Validated()
    {
        if (
            ContentId == Guid.Empty
            || Length is < 1 or > MaximumBytes
            || Sha256 is null
            || !IsSha256Hex(Sha256)
        )
        {
            throw new RuleException("InvalidDocument");
        }

        var name = Rules.Text(FileName, MaximumFileNameLength);

        if (!IsPlainFileName(name))
        {
            throw new RuleException("InvalidDocument");
        }

        return this with
        {
            FileName = name,
            Sha256 = Sha256.ToLowerInvariant(),
        };
    }

    private static bool IsSha256Hex(string value) =>
        value.Length == Sha256HexLength && value.All(char.IsAsciiHexDigit);

    // The name is shown and offered for download, so it must not be or contain a path.
    private static bool IsPlainFileName(string name)
    {
        if (name is "." or "..")
        {
            return false;
        }

        return !name.Any(character => char.IsControl(character) || character is '/' or '\\');
    }
}

/// <summary>
/// A document attached to an offer. Replacing the file adds a version; earlier versions remain
/// and stay linked to the offer version they were uploaded for.
/// </summary>
public sealed class OfferDocument
{
    private const int MaximumNameLength = 160;
    private const int MaximumRevisions = 100;

    private readonly List<DocumentRevision> revisions = [];

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EvaluationId { get; private set; }
    public Guid OfferId { get; private set; }
    public string Name { get; private set; } = "";
    public IReadOnlyList<DocumentRevision> Revisions => revisions.AsReadOnly();
    public DocumentRevision Latest =>
        revisions.MaxBy(revision => revision.Number) ?? throw new RuleException("NotFound");

    private OfferDocument() { }

    internal OfferDocument(Guid evaluationId, Guid offerId, string name)
    {
        EvaluationId = evaluationId;
        OfferId = offerId;
        Name = Rules.Text(name, MaximumNameLength);
    }

    internal void Append(
        int offerRevision,
        DocumentFile file,
        string? note,
        DateTimeOffset? createdAt = null
    )
    {
        var revision = new DocumentRevision(
            EvaluationId,
            OfferId,
            Id,
            revisions.Count + 1,
            offerRevision,
            file,
            note,
            createdAt
        );

        // Versions only move forward: neither in time nor across offer versions may one go back.
        var isFull = revisions.Count >= MaximumRevisions;
        var goesBackwards =
            revisions.Count > 0
            && (
                revision.CreatedAt < Latest.CreatedAt || offerRevision < Latest.OfferRevisionNumber
            );

        if (isFull || goesBackwards)
        {
            throw new RuleException("InvalidDocument");
        }

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

    internal DocumentRevision(
        Guid evaluationId,
        Guid offerId,
        Guid documentId,
        int number,
        int offerRevision,
        DocumentFile file,
        string? note,
        DateTimeOffset? createdAt
    )
    {
        EvaluationId = evaluationId;
        OfferId = offerId;
        DocumentId = documentId;
        Number = number;
        OfferRevisionNumber = offerRevision;
        File = file.Validated();
        Note = Rules.OptionalText(note, Rules.ShortNoteLength);
        CreatedAt = (createdAt ?? DateTimeOffset.UtcNow).ToUniversalTime();

        if (!Rules.IsAcceptableRecordDate(CreatedAt))
        {
            throw new RuleException("InvalidDocument");
        }
    }
}
