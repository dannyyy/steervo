namespace Steervo.Core;

public enum CandidateCondition
{
    Unknown,
    New,
    Used,
}

public enum CandidateStatus
{
    Watching,
    Alternative,
    Favorite,
    Deferred,
    Excluded,
}

/// <summary>Where the answer to a criterion comes from.</summary>
public enum AssessmentSource
{
    Unknown,
    Manufacturer,
    Offer,
    Inspection,
    TestDrive,
    Research,
}

public sealed record CandidateInput(
    string Model,
    string? Brand = null,
    string? Variant = null,
    CandidateCondition Condition = CandidateCondition.Unknown,
    int? Year = null,
    int? Mileage = null,
    string? Notes = null,
    string? Links = null,
    VehicleSpecifications? Specifications = null
);

/// <summary>A car under consideration in an evaluation.</summary>
public sealed class Candidate
{
    private const int MaximumBrandLength = 100;
    private const int MaximumVariantLength = 160;
    private const int MaximumYear = 9999;
    private const int MaximumLinks = 10;
    private const int MaximumLinkLength = 2000;
    private const int MaximumLinksLength = 12000;
    private const int MaximumStatusReasonLength = 2000;

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
    public string DisplayName =>
        string.Join(" ", new[] { Brand, Model, Variant }.Where(part => part.Length > 0));
    public IEnumerable<string> SourceLinks =>
        Links.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private Candidate() { }

    internal Candidate(Guid evaluationId, CandidateInput input, int position)
    {
        EvaluationId = evaluationId;
        Update(input);
        Position = position;
    }

    internal void Update(CandidateInput input)
    {
        // Everything is validated first so that a rejected change leaves the car untouched.
        var model = Rules.Text(input.Model);
        var brand = Rules.OptionalText(input.Brand, MaximumBrandLength);
        var variant = Rules.OptionalText(input.Variant, MaximumVariantLength);
        var condition = Rules.Defined(input.Condition);

        if (input.Year is < 1 or > MaximumYear || input.Mileage is < 0)
        {
            throw new RuleException("InvalidValue");
        }

        var notes = Rules.OptionalText(input.Notes, Rules.LongNoteLength);
        var links = Rules
            .OptionalText(input.Links, MaximumLinksLength)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (links.Length > MaximumLinks || links.Any(link => !IsAcceptableLink(link)))
        {
            throw new RuleException("InvalidCandidateLinks");
        }

        var specifications = input.Specifications ?? new VehicleSpecifications();
        specifications.Validate();

        Model = model;
        Brand = brand;
        Variant = variant;
        Condition = condition;
        Specifications = specifications;
        Year = input.Year;
        Mileage = input.Mileage;
        Notes = notes;
        Links = string.Join('\n', links);
    }

    internal void Classify(CandidateStatus status, string? reason)
    {
        Rules.Defined(status);
        var text = Rules.OptionalText(reason, MaximumStatusReasonLength);

        // Setting a car aside is a decision that has to be explained.
        var requiresReason = status is CandidateStatus.Deferred or CandidateStatus.Excluded;

        if (requiresReason && text.Length == 0)
        {
            throw new RuleException("CandidateReasonRequired");
        }

        Status = status;
        StatusReason = text;
    }

    internal CandidateInput CopyInput(string model) =>
        new(model, Brand, Variant, Condition, Year, Mileage, Notes, Links, Specifications with { });

    private static bool IsAcceptableLink(string link)
    {
        if (link.Length > MaximumLinkLength)
        {
            return false;
        }

        if (!Uri.TryCreate(link, UriKind.Absolute, out var address))
        {
            return false;
        }

        if (address.Scheme is not ("http" or "https"))
        {
            return false;
        }

        // Links are shown to everyone with access, so they must not carry credentials.
        return address.UserInfo.Length == 0;
    }
}

public sealed record AssessmentInput(
    bool? BooleanValue = null,
    decimal? MeasurementValue = null,
    int? RatingValue = null,
    AssessmentSource Source = AssessmentSource.Unknown,
    string? Notes = null,
    bool OptionalAvailable = false
)
{
    /// <summary>Rejects values that do not fit the criterion type or contradict each other.</summary>
    internal void RequireValuesFor(CriterionType type)
    {
        var answersYesNo = BooleanValue.HasValue || OptionalAvailable;
        var hasForeignValue =
            (type != CriterionType.Boolean && answersYesNo)
            || (type != CriterionType.Measurement && MeasurementValue.HasValue)
            || (type != CriterionType.Rating && RatingValue.HasValue);

        // "Available as an option" replaces a yes or no; both together would be ambiguous.
        var isAmbiguous = OptionalAvailable && BooleanValue.HasValue;
        var ratingOutOfRange = RatingValue is < Rules.MinimumRating or > Rules.MaximumRating;

        if (hasForeignValue || isAmbiguous || ratingOutOfRange)
        {
            throw new RuleException("InvalidValue");
        }
    }
}

/// <summary>The answer to one vehicle criterion for one car.</summary>
public sealed class Assessment
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EvaluationId { get; private set; }
    public Guid CandidateId { get; private set; }
    public Guid CriterionId { get; private set; }
    public bool? BooleanValue { get; private set; }
    public bool OptionalAvailable { get; private set; }
    public decimal? MeasurementValue { get; private set; }
    public int? RatingValue { get; private set; }
    public AssessmentSource Source { get; private set; }
    public string Notes { get; private set; } = "";
    public bool HasValue =>
        OptionalAvailable
        || BooleanValue.HasValue
        || MeasurementValue.HasValue
        || RatingValue.HasValue;

    private Assessment() { }

    internal Assessment(
        Guid evaluationId,
        Guid candidateId,
        Criterion criterion,
        AssessmentInput input
    )
    {
        EvaluationId = evaluationId;
        CandidateId = candidateId;
        CriterionId = criterion.Id;
        Update(criterion, input);
    }

    internal void Update(Criterion criterion, AssessmentInput input)
    {
        if (criterion.Scope != CriterionScope.Vehicle)
        {
            throw new RuleException("VehicleCriteriaOnly");
        }

        input.RequireValuesFor(criterion.Type);
        var source = Rules.Defined(input.Source);
        var notes = Rules.OptionalText(input.Notes, Rules.LongNoteLength);

        BooleanValue = input.BooleanValue;
        OptionalAvailable = input.OptionalAvailable;
        MeasurementValue = input.MeasurementValue;
        RatingValue = input.RatingValue;
        Source = source;
        Notes = notes;
    }

    internal AssessmentInput CopyInput() =>
        new(BooleanValue, MeasurementValue, RatingValue, Source, Notes, OptionalAvailable);
}
