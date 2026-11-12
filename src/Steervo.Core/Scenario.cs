namespace Steervo.Core;

public enum AcquisitionMode { Purchase, LeaseReturn, LeaseBuyout, Financing }

public sealed record ScenarioTerms
{
    public AcquisitionMode Mode { get; init; }
    public decimal? PurchasePrice { get; init; }
    public decimal? InitialFees { get; init; }
    public decimal? DownPayment { get; init; }
    public decimal? MonthlyPayment { get; init; }
    public int? TermMonths { get; init; }
    public decimal? FinalPayment { get; init; }
    public decimal? LoanAmount { get; init; }
    public decimal? LoanInterestPercent { get; init; }
    public int? AnnualKilometres { get; init; }
    public int? IncludedKilometresPerYear { get; init; }
    public decimal? ExcessKilometrePrice { get; init; }
    public decimal? UnusedKilometreCredit { get; init; }
    public bool CostsReviewed { get; init; }

    internal ScenarioTerms Validated()
    {
        Rules.Defined(Mode);
        foreach (var amount in new[] { PurchasePrice, InitialFees, DownPayment, MonthlyPayment, FinalPayment, LoanAmount }) ScenarioRules.Money(amount);
        if (TermMonths is < 1 or > 1200 || LoanInterestPercent is < 0 or > 100
            || AnnualKilometres is < 0 or > 1_000_000 || IncludedKilometresPerYear is < 0 or > 1_000_000
            || ExcessKilometrePrice is < 0 or > 10000 || UnusedKilometreCredit is < 0 or > 10000
            || (Mode == AcquisitionMode.Financing && LoanAmount > PurchasePrice)) throw new RuleException("InvalidScenario");
        return this;
    }
}
public sealed record ScenarioInput(string Name, Guid OfferId, int RevisionNumber, ScenarioTerms Terms, string? Notes = null);
public sealed record ResidualInput(int Month, decimal? Low, decimal? Expected, decimal? High, string? Notes = null)
{
    internal ResidualInput Validated()
    {
        ScenarioRules.Money(Low); ScenarioRules.Money(Expected); ScenarioRules.Money(High);
        if (Month is < 1 or > 1200 || Low > Expected || Expected > High || Low > High) throw new RuleException("InvalidResidual");
        return this with { Notes = Rules.OptionalText(Notes, 2000) };
    }
}
public sealed record CostAdjustmentInput(Guid CostId, decimal? Amount, int StartMonth = 1, int? EndMonth = null, string? Notes = null)
{
    internal CostAdjustmentInput Validated()
    {
        ScenarioRules.Money(Amount);
        if (CostId == Guid.Empty || StartMonth is < 1 or > 1200 || EndMonth is < 1 or > 1200 || EndMonth < StartMonth) throw new RuleException("InvalidCostAdjustment");
        return this with { Notes = Rules.OptionalText(Notes, 2000) };
    }
}
internal static class ScenarioRules
{
    internal static void Money(decimal? amount)
    {
        if (amount is < 0 or > 1_000_000_000_000m || (amount.HasValue && decimal.Round(amount.Value, 2) != amount)) throw new RuleException("InvalidScenario");
    }
}

public sealed class Scenario
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EvaluationId { get; private set; }
    public Guid OfferId { get; private set; }
    public int RevisionNumber { get; private set; }
    public string Name { get; private set; } = "";
    public string Notes { get; private set; } = "";
    public ScenarioTerms Terms { get; private set; } = new();
    public List<ResidualCheckpoint> Residuals { get; private set; } = [];
    public List<ScenarioCostAdjustment> CostAdjustments { get; private set; } = [];
    private Scenario() { }
    internal Scenario(Guid evaluationId, ScenarioInput input) { EvaluationId = evaluationId; Update(input); }
    internal void Update(ScenarioInput input)
    {
        var name = Rules.Text(input.Name); var notes = Rules.OptionalText(input.Notes, 4000); var terms = input.Terms.Validated();
        Name = name; Notes = notes; Terms = terms; OfferId = input.OfferId; RevisionNumber = input.RevisionNumber;
    }
    internal void SetResidual(ResidualInput input)
    {
        var valid = input.Validated(); var checkpoint = Residuals.SingleOrDefault(r => r.Month == input.Month);
        if (checkpoint is null)
        {
            if (Residuals.Count >= 100) throw new RuleException("TooManyResiduals");
            Residuals.Add(new(Id, valid));
        }
        else checkpoint.Update(valid);
    }
    internal void SetAdjustment(CostAdjustmentInput input)
    {
        var valid = input.Validated(); var item = CostAdjustments.SingleOrDefault(c => c.CostId == input.CostId);
        if (item is null) CostAdjustments.Add(new(EvaluationId, Id, valid));
        else item.Update(valid);
    }
    public ScenarioInput ToInput() => new(Name, OfferId, RevisionNumber, Terms, Notes);
}

public sealed class ResidualCheckpoint
{
    public Guid ScenarioId { get; private set; }
    public int Month { get; private set; }
    public decimal? Low { get; private set; }
    public decimal? Expected { get; private set; }
    public decimal? High { get; private set; }
    public string Notes { get; private set; } = "";
    private ResidualCheckpoint() { }
    internal ResidualCheckpoint(Guid scenarioId, ResidualInput input) { ScenarioId = scenarioId; Month = input.Month; Update(input); }
    internal void Update(ResidualInput input) { Low = input.Low; Expected = input.Expected; High = input.High; Notes = input.Notes ?? ""; }
    public ResidualInput ToInput() => new(Month, Low, Expected, High, Notes);
}
public sealed class ScenarioCostAdjustment
{
    public Guid EvaluationId { get; private set; }
    public Guid ScenarioId { get; private set; }
    public Guid CostId { get; private set; }
    public decimal? Amount { get; private set; }
    public int StartMonth { get; private set; }
    public int? EndMonth { get; private set; }
    public string Notes { get; private set; } = "";
    private ScenarioCostAdjustment() { }
    internal ScenarioCostAdjustment(Guid evaluationId, Guid scenarioId, CostAdjustmentInput input) { EvaluationId = evaluationId; ScenarioId = scenarioId; CostId = input.CostId; Update(input); }
    internal void Update(CostAdjustmentInput input) { Amount = input.Amount; StartMonth = input.StartMonth; EndMonth = input.EndMonth; Notes = input.Notes ?? ""; }
    public CostAdjustmentInput ToInput() => new(CostId, Amount, StartMonth, EndMonth, Notes);
}

public sealed class CalculationSettings
{
    public Guid EvaluationId { get; private set; }
    public decimal AnnualReturnPercent { get; private set; }
    private CalculationSettings() { }
    internal CalculationSettings(Guid evaluationId, decimal annualReturnPercent) { EvaluationId = evaluationId; Update(annualReturnPercent); }
    internal void Update(decimal annualReturnPercent)
    {
        if (annualReturnPercent is < -99 or > 100) throw new RuleException("InvalidReturn");
        AnnualReturnPercent = annualReturnPercent;
    }
}
