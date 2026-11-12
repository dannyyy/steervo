namespace Steervo.Core;

public enum CostFrequency { Once, Monthly, Yearly, Included }
public enum CostDirection { Expense, Income }

public sealed record CostInput(string Name, CostCategory Category, CostFrequency Frequency, decimal? Amount,
    int StartMonth = 1, int? EndMonth = null, Guid? CandidateId = null, CostDirection Direction = CostDirection.Expense, string? Notes = null)
{
    internal CostInput Validated()
    {
        Rules.Defined(Category); Rules.Defined(Frequency); Rules.Defined(Direction);
        if (StartMonth is < 1 or > 1200 || EndMonth is < 1 or > 1200 || EndMonth < StartMonth
            || Amount is < 0 or > 1_000_000_000_000m || (Amount.HasValue && decimal.Round(Amount.Value, 2) != Amount)
            || (Frequency == CostFrequency.Once && EndMonth.HasValue)
            || (Frequency == CostFrequency.Included && (Amount.HasValue || Direction != CostDirection.Expense)))
            throw new RuleException("InvalidCost");
        return this with { Name = Rules.Text(Name, 200), Notes = Rules.OptionalText(Notes, 2000) };
    }
}

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
    internal CostItem(Guid evaluationId, CostInput input) { EvaluationId = evaluationId; Update(input); }
    internal void Update(CostInput input)
    {
        var valid = input.Validated();
        Name = valid.Name; Category = valid.Category; Frequency = valid.Frequency; Direction = valid.Direction;
        Amount = valid.Amount; StartMonth = valid.StartMonth; EndMonth = valid.EndMonth; CandidateId = valid.CandidateId; Notes = valid.Notes ?? "";
    }
    public CostInput ToInput() => new(Name, Category, Frequency, Amount, StartMonth, EndMonth, CandidateId, Direction, Notes);
}

public sealed class CostPlanning
{
    public Guid EvaluationId { get; private set; }
    public DateOnly Start { get; private set; }
    public int Months { get; private set; }
    private CostPlanning() { }
    internal CostPlanning(Guid evaluationId, DateOnly start, int months) { EvaluationId = evaluationId; Update(start, months); }
    internal void Update(DateOnly start, int months)
    {
        if (start.Day != 1 || start.Year is < 1900 or > 9899 || months is < 1 or > 1200) throw new RuleException("InvalidCostPlanning");
        Start = start; Months = months;
    }
    public DateOnly DateFor(int month) => month is >= 1 and <= 1200 ? Start.AddMonths(month - 1) : throw new RuleException("InvalidCostPlanning");
}
