namespace Steervo.Core;

public enum CandidateCondition { Unknown, New, Used }
public enum CandidateStatus { Watching, Alternative, Favorite, Deferred, Excluded }
public enum AssessmentSource { Unknown, Manufacturer, Offer, Inspection, TestDrive, Research }

public sealed record CandidateInput(string Model, string? Brand = null, string? Variant = null,
    CandidateCondition Condition = CandidateCondition.Unknown, int? Year = null, int? Mileage = null,
    string? Notes = null, string? Links = null, VehicleSpecifications? Specifications = null);

public sealed class Candidate
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EvaluationId { get; private set; }
    public string Brand { get; private set; } = "";
    public string Model { get; private set; } = "";
    public string Variant { get; private set; } = "";
    public CandidateCondition Condition { get; private set; }
    public int? Year { get; private set; }
    public int? Mileage { get; private set; }
    public VehicleSpecifications Specifications { get; private set; } = new();
    public string Notes { get; private set; } = "";
    public string Links { get; private set; } = "";
    public CandidateStatus Status { get; private set; } = CandidateStatus.Watching;
    public string StatusReason { get; private set; } = "";
    public int Position { get; internal set; }
    public string DisplayName => string.Join(" ", new[] { Brand, Model, Variant }.Where(x => x.Length > 0));
    public IEnumerable<string> SourceLinks => Links.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private Candidate() { }
    internal Candidate(Guid evaluationId, CandidateInput input, int position)
    {
        EvaluationId = evaluationId;
        Update(input);
        Position = position;
    }
    internal void Update(CandidateInput input)
    {
        var model = Rules.Text(input.Model);
        var brand = Rules.OptionalText(input.Brand, 100);
        var variant = Rules.OptionalText(input.Variant, 160);
        var condition = Rules.Defined(input.Condition);
        if (input.Year is < 1 or > 9999 || input.Mileage is < 0) throw new RuleException("InvalidValue");
        var notes = Rules.OptionalText(input.Notes, 4000);
        var links = Rules.OptionalText(input.Links, 12000).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (links.Length > 10 || links.Any(link => link.Length > 2000 || !Uri.TryCreate(link, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0)) throw new RuleException("InvalidCandidateLinks");
        var specifications = input.Specifications ?? new VehicleSpecifications();
        specifications.Validate();
        Model = model; Brand = brand; Variant = variant; Condition = condition; Specifications = specifications;
        Year = input.Year; Mileage = input.Mileage; Notes = notes; Links = string.Join('\n', links);
    }
    internal void Classify(CandidateStatus status, string? reason)
    {
        Rules.Defined(status);
        var text = Rules.OptionalText(reason, 2000);
        if (status is CandidateStatus.Deferred or CandidateStatus.Excluded && text.Length == 0) throw new RuleException("CandidateReasonRequired");
        Status = status; StatusReason = text;
    }
    internal CandidateInput CopyInput(string model) => new(model, Brand, Variant, Condition, Year, Mileage, Notes, Links, Specifications with { });
}

public sealed record AssessmentInput(bool? BooleanValue = null, decimal? MeasurementValue = null, int? RatingValue = null,
    AssessmentSource Source = AssessmentSource.Unknown, string? Notes = null);

public sealed class Assessment
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EvaluationId { get; private set; }
    public Guid CandidateId { get; private set; }
    public Guid CriterionId { get; private set; }
    public bool? BooleanValue { get; private set; }
    public decimal? MeasurementValue { get; private set; }
    public int? RatingValue { get; private set; }
    public AssessmentSource Source { get; private set; }
    public string Notes { get; private set; } = "";
    public bool HasValue => BooleanValue.HasValue || MeasurementValue.HasValue || RatingValue.HasValue;

    private Assessment() { }
    internal Assessment(Guid evaluationId, Guid candidateId, Criterion criterion, AssessmentInput input)
    {
        EvaluationId = evaluationId; CandidateId = candidateId; CriterionId = criterion.Id;
        Update(criterion, input);
    }
    internal void Update(Criterion criterion, AssessmentInput input)
    {
        if (criterion.Scope != CriterionScope.Vehicle) throw new RuleException("VehicleCriteriaOnly");
        if ((criterion.Type != CriterionType.Boolean && input.BooleanValue.HasValue)
            || (criterion.Type != CriterionType.Measurement && input.MeasurementValue.HasValue)
            || (criterion.Type != CriterionType.Rating && input.RatingValue.HasValue)
            || input.RatingValue is < 1 or > 5) throw new RuleException("InvalidValue");
        var source = Rules.Defined(input.Source);
        var notes = Rules.OptionalText(input.Notes, 4000);
        BooleanValue = input.BooleanValue; MeasurementValue = input.MeasurementValue; RatingValue = input.RatingValue;
        Source = source; Notes = notes;
    }
    internal AssessmentInput CopyInput() => new(BooleanValue, MeasurementValue, RatingValue, Source, Notes);
}
