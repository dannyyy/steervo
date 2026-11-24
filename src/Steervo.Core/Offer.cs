namespace Steervo.Core;

/// <summary>
/// An offer for one car. It is never edited in place: every change adds a numbered version, so
/// earlier states stay available for comparison.
/// </summary>
public sealed class Offer
{
    private readonly List<OfferRevision> revisions = [];

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EvaluationId { get; private set; }
    public Guid CandidateId { get; private set; }
    public int Position { get; internal set; }
    public IReadOnlyList<OfferRevision> Revisions => revisions.AsReadOnly();
    public OfferRevision Latest =>
        revisions.MaxBy(revision => revision.Number) ?? throw new RuleException("NotFound");

    private Offer() { }

    internal Offer(
        Guid evaluationId,
        Guid candidateId,
        OfferTerms terms,
        IEnumerable<OfferLineInput> lines,
        string? changeNote,
        DateTimeOffset? createdAt = null
    )
    {
        EvaluationId = evaluationId;
        CandidateId = candidateId;
        Revise(terms, lines, changeNote, createdAt);
    }

    public OfferRevision Revision(int number) =>
        revisions.SingleOrDefault(revision => revision.Number == number)
        ?? throw new RuleException("NotFound");

    internal void Revise(
        OfferTerms terms,
        IEnumerable<OfferLineInput> lines,
        string? changeNote,
        DateTimeOffset? createdAt = null
    )
    {
        var revision = new OfferRevision(
            EvaluationId,
            Id,
            revisions.Count + 1,
            terms,
            lines,
            changeNote,
            createdAt
        );

        if (revisions.Count > 0)
        {
            var latest = Latest;

            if (RepeatsWithoutNote(revision, latest))
            {
                throw new RuleException("NoOfferChanges");
            }

            if (revision.CreatedAt < latest.CreatedAt)
            {
                throw new RuleException("InvalidOfferDate");
            }
        }

        revisions.Add(revision);
    }

    // A new version must differ from the previous one, or at least say why it was recorded.
    private static bool RepeatsWithoutNote(OfferRevision revision, OfferRevision latest)
    {
        if (revision.Terms != latest.Terms)
        {
            return false;
        }

        var newLines = revision.Lines.Select(line => line.ToInput());
        var latestLines = latest
            .Lines.OrderBy(line => line.Position)
            .Select(line => line.ToInput());

        return newLines.SequenceEqual(latestLines) && revision.ChangeNote.Length == 0;
    }
}

/// <summary>One version of an offer: its terms and its priced lines at a point in time.</summary>
public sealed class OfferRevision
{
    internal const int MaximumLines = 500;

    // "Price" is reported after at most this many of the fields missing from the terms.
    private const int PricePositionAmongMissingFields = 3;

    private readonly List<OfferLine> lines = [];

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EvaluationId { get; private set; }
    public Guid OfferId { get; private set; }
    public int Number { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public string ChangeNote { get; private set; } = "";
    public OfferTerms Terms { get; private set; } = new();
    public IReadOnlyList<OfferLine> Lines => lines.AsReadOnly();
    public OfferPricing Pricing => OfferPricing.From(lines.Select(line => line.ToInput()));
    public InstalmentCheck? InstalmentCheck => InstalmentCheck.From(Terms, Pricing);

    private OfferRevision() { }

    internal OfferRevision(
        Guid evaluationId,
        Guid offerId,
        int number,
        OfferTerms terms,
        IEnumerable<OfferLineInput> input,
        string? changeNote,
        DateTimeOffset? createdAt = null
    )
    {
        CreatedAt = (createdAt ?? DateTimeOffset.UtcNow).ToUniversalTime();

        if (!Rules.IsAcceptableRecordDate(CreatedAt))
        {
            throw new RuleException("InvalidOfferDate");
        }

        Terms = terms.Validated();
        ChangeNote = Rules.OptionalText(changeNote, Rules.ShortNoteLength);
        EvaluationId = evaluationId;
        OfferId = offerId;
        Number = number;

        // Reading one line beyond the limit is enough to notice that there are too many.
        var lineInputs = input.Take(MaximumLines + 1).ToArray();

        if (lineInputs.Length > MaximumLines)
        {
            throw new RuleException("TooManyOfferLines");
        }

        for (var position = 0; position < lineInputs.Length; position++)
        {
            lines.Add(new OfferLine(Id, position, lineInputs[position]));
        }
    }

    /// <summary>Names of the details still needed before the offer can be compared with others.</summary>
    public IEnumerable<string> MissingFields()
    {
        var missing = Terms.MissingFields().ToList();
        var hasPricedLine = lines.Any(line =>
            line.PriceKind == OfferPriceKind.Amount && line.Kind != OfferLineKind.Discount
        );

        if (!hasPricedLine)
        {
            var pricePosition = Math.Min(missing.Count, PricePositionAmongMissingFields);
            missing.Insert(pricePosition, "Price");
        }

        return missing;
    }
}

/// <summary>A single position of an offer version: the car, an extra, a fee, a benefit or a discount.</summary>
public sealed class OfferLine
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid RevisionId { get; private set; }
    public int Position { get; private set; }
    public string Name { get; private set; } = "";
    public OfferLineKind Kind { get; private set; }
    public OfferPriceKind PriceKind { get; private set; }
    public decimal? Amount { get; private set; }
    public OfferBenefitEffect? Effect { get; private set; }
    public CostCategory? CostCategory { get; private set; }
    public int? DurationMonths { get; private set; }
    public string Notes { get; private set; } = "";
    public bool Financed { get; private set; }
    public decimal? Value { get; private set; }
    public Guid? CriterionId { get; private set; }

    private OfferLine() { }

    internal OfferLine(Guid revisionId, int position, OfferLineInput input)
    {
        var valid = input.Validated();

        RevisionId = revisionId;
        Position = position;
        Name = valid.Name;
        Kind = valid.Kind;
        PriceKind = valid.PriceKind;
        Amount = valid.Amount;
        Effect = valid.Effect;
        CostCategory = valid.CostCategory;
        DurationMonths = valid.DurationMonths;
        Notes = valid.Notes ?? "";
        Financed = valid.Financed;
        Value = valid.Value;
        CriterionId = valid.CriterionId;
    }

    public OfferLineInput ToInput() =>
        new(
            Name,
            Kind,
            PriceKind,
            Amount,
            Effect,
            CostCategory,
            DurationMonths,
            Notes,
            Financed,
            Value,
            CriterionId
        );
}
