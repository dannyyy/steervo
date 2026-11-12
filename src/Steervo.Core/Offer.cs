namespace Steervo.Core;

public sealed class Offer
{
    private readonly List<OfferRevision> revisions = [];
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EvaluationId { get; private set; }
    public Guid CandidateId { get; private set; }
    public bool Archived { get; private set; }
    public IReadOnlyList<OfferRevision> Revisions => revisions.AsReadOnly();
    public OfferRevision Latest => revisions.MaxBy(revision => revision.Number) ?? throw new RuleException("NotFound");
    private Offer() { }
    internal Offer(Guid evaluationId, Guid candidateId, OfferTerms terms, IEnumerable<OfferLineInput> lines, string? changeNote, DateTimeOffset? createdAt = null)
    {
        EvaluationId = evaluationId; CandidateId = candidateId;
        Revise(terms, lines, changeNote, createdAt);
    }
    public OfferRevision Revision(int number) => revisions.SingleOrDefault(revision => revision.Number == number) ?? throw new RuleException("NotFound");
    internal void Revise(OfferTerms terms, IEnumerable<OfferLineInput> lines, string? changeNote, DateTimeOffset? createdAt = null)
    {
        if (Archived) throw new RuleException("OfferArchived");
        var revision = new OfferRevision(EvaluationId, Id, revisions.Count + 1, terms, lines, changeNote, createdAt);
        if (revisions.Count > 0 && revision.Terms == Latest.Terms
            && revision.Lines.Select(line => line.ToInput()).SequenceEqual(Latest.Lines.OrderBy(line => line.Position).Select(line => line.ToInput()))
            && revision.ChangeNote.Length == 0) throw new RuleException("NoOfferChanges");
        if (revisions.Count > 0 && revision.CreatedAt < Latest.CreatedAt) throw new RuleException("InvalidOfferDate");
        revisions.Add(revision);
    }
    internal void SetArchived(bool archived) => Archived = archived;
}

public sealed class OfferRevision
{
    private readonly List<OfferLine> lines = [];
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EvaluationId { get; private set; }
    public Guid OfferId { get; private set; }
    public int Number { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public string ChangeNote { get; private set; } = "";
    public OfferTerms Terms { get; private set; } = new();
    public IReadOnlyList<OfferLine> Lines => lines.AsReadOnly();
    private OfferRevision() { }
    internal OfferRevision(Guid evaluationId, Guid offerId, int number, OfferTerms terms, IEnumerable<OfferLineInput> input, string? changeNote, DateTimeOffset? createdAt = null)
    {
        CreatedAt = (createdAt ?? DateTimeOffset.UtcNow).ToUniversalTime();
        if (CreatedAt.Year < 1900 || CreatedAt > DateTimeOffset.UtcNow.AddMinutes(5)) throw new RuleException("InvalidOfferDate");
        Terms = terms.Validated(); ChangeNote = Rules.OptionalText(changeNote, 2000);
        EvaluationId = evaluationId; OfferId = offerId; Number = number;
        var positions = input.Take(501).ToArray();
        if (positions.Length > 500) throw new RuleException("TooManyOfferLines");
        for (var index = 0; index < positions.Length; index++) lines.Add(new OfferLine(Id, index, positions[index]));
    }
}

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
    private OfferLine() { }
    internal OfferLine(Guid revisionId, int position, OfferLineInput input)
    {
        var valid = input.Validated();
        RevisionId = revisionId; Position = position; Name = valid.Name; Kind = valid.Kind;
        PriceKind = valid.PriceKind; Amount = valid.Amount; Effect = valid.Effect;
        CostCategory = valid.CostCategory; DurationMonths = valid.DurationMonths; Notes = valid.Notes ?? "";
    }
    public OfferLineInput ToInput() => new(Name, Kind, PriceKind, Amount, Effect, CostCategory, DurationMonths, Notes);
}
