namespace Steervo.Core;

public enum CostFrequency
{
    Once,
    Monthly,
    Yearly,
    Included,
}

public enum CostDirection
{
    Expense,
    Income,
}

public sealed record CostInput(
    string Name,
    CostCategory Category,
    CostFrequency Frequency,
    decimal? Amount,
    int StartMonth = 1,
    int? EndMonth = null,
    Guid? CandidateId = null,
    CostDirection Direction = CostDirection.Expense,
    string? Notes = null
)
{
    private const int MaximumNameLength = 200;

    internal CostInput Validated()
    {
        Rules.Defined(Category);
        Rules.Defined(Frequency);
        Rules.Defined(Direction);

        var periodIsInvalid =
            StartMonth is < 1 or > Rules.MaximumMonths
            || EndMonth is < 1 or > Rules.MaximumMonths
            || EndMonth < StartMonth;
        var amountIsInvalid =
            Amount is < 0 or > Rules.MaximumAmount
            || (Amount.HasValue && decimal.Round(Amount.Value, 2) != Amount);

        // A one-off cost has no period, and a cost that is already included elsewhere has no
        // amount of its own and cannot be income.
        var oneOffHasEnd = Frequency == CostFrequency.Once && EndMonth.HasValue;
        var includedIsPriced =
            Frequency == CostFrequency.Included
            && (Amount.HasValue || Direction != CostDirection.Expense);

        if (periodIsInvalid || amountIsInvalid || oneOffHasEnd || includedIsPriced)
        {
            throw new RuleException("InvalidCost");
        }

        return this with
        {
            Name = Rules.Text(Name, MaximumNameLength),
            Notes = Rules.OptionalText(Notes, Rules.ShortNoteLength),
        };
    }
}

/// <summary>A running or one-off cost, either for every car or for one car only.</summary>
public sealed class CostItem
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EvaluationId { get; private set; }
    public Guid? CandidateId { get; private set; }
    public string Name { get; private set; } = "";
    public CostCategory Category { get; private set; }
    public CostFrequency Frequency { get; private set; }
    public CostDirection Direction { get; private set; }
    public decimal? Amount { get; private set; }
    public int StartMonth { get; private set; }
    public int? EndMonth { get; private set; }
    public string Notes { get; private set; } = "";

    private CostItem() { }

    internal CostItem(Guid evaluationId, CostInput input)
    {
        EvaluationId = evaluationId;
        Update(input);
    }

    internal void Update(CostInput input)
    {
        var valid = input.Validated();

        Name = valid.Name;
        Category = valid.Category;
        Frequency = valid.Frequency;
        Direction = valid.Direction;
        Amount = valid.Amount;
        StartMonth = valid.StartMonth;
        EndMonth = valid.EndMonth;
        CandidateId = valid.CandidateId;
        Notes = valid.Notes ?? "";
    }

    public CostInput ToInput() =>
        new(Name, Category, Frequency, Amount, StartMonth, EndMonth, CandidateId, Direction, Notes);
}

/// <summary>The period all costs of an evaluation are planned over, counted in months from its start.</summary>
public sealed class CostPlanning
{
    // The latest start still leaves room for the longest plan before the calendar ends.
    private const int EarliestStartYear = 1900;
    private const int LatestStartYear = 9899;

    public Guid EvaluationId { get; private set; }
    public DateOnly Start { get; private set; }
    public int Months { get; private set; }

    private CostPlanning() { }

    internal CostPlanning(Guid evaluationId, DateOnly start, int months)
    {
        EvaluationId = evaluationId;
        Update(start, months);
    }

    internal void Update(DateOnly start, int months)
    {
        var startsOnFirstOfMonth = start.Day == 1;
        var startYearIsInvalid = start.Year is < EarliestStartYear or > LatestStartYear;
        var monthsAreInvalid = months is < 1 or > Rules.MaximumMonths;

        if (!startsOnFirstOfMonth || startYearIsInvalid || monthsAreInvalid)
        {
            throw new RuleException("InvalidCostPlanning");
        }

        Start = start;
        Months = months;
    }

    /// <summary>The first day of the given plan month; month 1 is the start itself.</summary>
    public DateOnly DateFor(int month)
    {
        if (month is < 1 or > Rules.MaximumMonths)
        {
            throw new RuleException("InvalidCostPlanning");
        }

        return Start.AddMonths(month - 1);
    }
}
