namespace Steervo.Core;

public enum AcquisitionMode
{
    Purchase,
    LeaseReturn,
    LeaseBuyout,
    Financing,
}

/// <summary>The assumptions one scenario calculates with: how the car is paid for and how much it is driven.</summary>
public sealed record ScenarioTerms
{
    private const decimal MaximumInterestPercent = 100;
    private const decimal MaximumKilometrePrice = 10000;

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

        var amounts = new[]
        {
            PurchasePrice,
            InitialFees,
            DownPayment,
            MonthlyPayment,
            FinalPayment,
            LoanAmount,
        };

        foreach (var amount in amounts)
        {
            ScenarioRules.Money(amount);
        }

        var numberOutOfRange =
            TermMonths is < 1 or > Rules.MaximumMonths
            || LoanInterestPercent is < 0 or > MaximumInterestPercent
            || AnnualKilometres is < 0 or > Rules.MaximumKilometresPerYear
            || IncludedKilometresPerYear is < 0 or > Rules.MaximumKilometresPerYear
            || ExcessKilometrePrice is < 0 or > MaximumKilometrePrice
            || UnusedKilometreCredit is < 0 or > MaximumKilometrePrice;
        var loanExceedsPrice = Mode == AcquisitionMode.Financing && LoanAmount > PurchasePrice;

        if (numberOutOfRange || loanExceedsPrice)
        {
            throw new RuleException("InvalidScenario");
        }

        return this;
    }
}

public sealed record ScenarioInput(
    string Name,
    Guid OfferId,
    int RevisionNumber,
    ScenarioTerms Terms,
    string? Notes = null
);

/// <summary>An estimate of what the car is still worth after a number of months, as a range.</summary>
public sealed record ResidualInput(
    int Month,
    decimal? Low,
    decimal? Expected,
    decimal? High,
    string? Notes = null
)
{
    internal ResidualInput Validated()
    {
        ScenarioRules.Money(Low);
        ScenarioRules.Money(Expected);
        ScenarioRules.Money(High);

        var monthOutOfRange = Month is < 1 or > Rules.MaximumMonths;
        var rangeIsOutOfOrder = Low > Expected || Expected > High || Low > High;

        if (monthOutOfRange || rangeIsOutOfOrder)
        {
            throw new RuleException("InvalidResidual");
        }

        return this with
        {
            Notes = Rules.OptionalText(Notes, Rules.ShortNoteLength),
        };
    }
}

public sealed record CostAdjustmentInput(
    Guid CostId,
    decimal? Amount,
    int StartMonth = 1,
    int? EndMonth = null,
    string? Notes = null
)
{
    internal CostAdjustmentInput Validated()
    {
        ScenarioRules.Money(Amount);

        var periodIsInvalid =
            StartMonth is < 1 or > Rules.MaximumMonths
            || EndMonth is < 1 or > Rules.MaximumMonths
            || EndMonth < StartMonth;

        if (CostId == Guid.Empty || periodIsInvalid)
        {
            throw new RuleException("InvalidCostAdjustment");
        }

        return this with
        {
            Notes = Rules.OptionalText(Notes, Rules.ShortNoteLength),
        };
    }
}

internal static class ScenarioRules
{
    // Scenario amounts are plain currency values: not negative and with at most two decimals.
    internal static void Money(decimal? amount)
    {
        var isOutOfRange = amount is < 0 or > Rules.MaximumAmount;
        var hasFractionsOfCents = amount.HasValue && decimal.Round(amount.Value, 2) != amount;

        if (isOutOfRange || hasFractionsOfCents)
        {
            throw new RuleException("InvalidScenario");
        }
    }
}

/// <summary>
/// One way of acquiring and running the car of a specific offer version, calculated over the
/// planning period.
/// </summary>
public sealed class Scenario
{
    private const int MaximumResiduals = 100;

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

    internal Scenario(Guid evaluationId, ScenarioInput input)
    {
        EvaluationId = evaluationId;
        Update(input);
    }

    internal void Update(ScenarioInput input)
    {
        var name = Rules.Text(input.Name);
        var notes = Rules.OptionalText(input.Notes, Rules.LongNoteLength);
        var terms = input.Terms.Validated();

        Name = name;
        Notes = notes;
        Terms = terms;
        OfferId = input.OfferId;
        RevisionNumber = input.RevisionNumber;
    }

    // There is one residual estimate per month: setting it again replaces the existing one.
    internal void SetResidual(ResidualInput input)
    {
        var valid = input.Validated();
        var checkpoint = Residuals.SingleOrDefault(residual => residual.Month == input.Month);

        if (checkpoint is not null)
        {
            checkpoint.Update(valid);
            return;
        }

        if (Residuals.Count >= MaximumResiduals)
        {
            throw new RuleException("TooManyResiduals");
        }

        Residuals.Add(new(Id, valid));
    }

    // There is one adjustment per cost: setting it again replaces the existing one.
    internal void SetAdjustment(CostAdjustmentInput input)
    {
        var valid = input.Validated();
        var existing = CostAdjustments.SingleOrDefault(adjustment =>
            adjustment.CostId == input.CostId
        );

        if (existing is null)
        {
            CostAdjustments.Add(new(EvaluationId, Id, valid));
        }
        else
        {
            existing.Update(valid);
        }
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

    internal ResidualCheckpoint(Guid scenarioId, ResidualInput input)
    {
        ScenarioId = scenarioId;
        Month = input.Month;
        Update(input);
    }

    internal void Update(ResidualInput input)
    {
        Low = input.Low;
        Expected = input.Expected;
        High = input.High;
        Notes = input.Notes ?? "";
    }

    public ResidualInput ToInput() => new(Month, Low, Expected, High, Notes);
}

/// <summary>Replaces the amount or period of one planned cost within a single scenario.</summary>
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

    internal ScenarioCostAdjustment(Guid evaluationId, Guid scenarioId, CostAdjustmentInput input)
    {
        EvaluationId = evaluationId;
        ScenarioId = scenarioId;
        CostId = input.CostId;
        Update(input);
    }

    internal void Update(CostAdjustmentInput input)
    {
        Amount = input.Amount;
        StartMonth = input.StartMonth;
        EndMonth = input.EndMonth;
        Notes = input.Notes ?? "";
    }

    public CostAdjustmentInput ToInput() => new(CostId, Amount, StartMonth, EndMonth, Notes);
}

/// <summary>Assumptions that apply to every scenario calculation of an evaluation.</summary>
public sealed class CalculationSettings
{
    private const decimal MinimumReturnPercent = -99;
    private const decimal MaximumReturnPercent = 100;

    public Guid EvaluationId { get; private set; }
    public decimal AnnualReturnPercent { get; private set; }

    private CalculationSettings() { }

    internal CalculationSettings(Guid evaluationId, decimal annualReturnPercent)
    {
        EvaluationId = evaluationId;
        Update(annualReturnPercent);
    }

    internal void Update(decimal annualReturnPercent)
    {
        if (annualReturnPercent is < MinimumReturnPercent or > MaximumReturnPercent)
        {
            throw new RuleException("InvalidReturn");
        }

        AnnualReturnPercent = annualReturnPercent;
    }
}
