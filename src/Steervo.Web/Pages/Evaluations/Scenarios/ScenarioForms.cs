using System.Globalization;
using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations.Scenarios;

/// <summary>
/// Describes one numeric input of the scenario editor. <see cref="Name"/> is the name of the
/// <see cref="ScenarioTerms"/> property it edits; <see cref="Modes"/> lists the acquisition modes
/// in which the input is shown.
/// </summary>
public sealed record ScenarioField(
    string Name,
    string Modes,
    string Step = "0.01",
    string Max = "1000000000000"
)
{
    /// <summary>
    /// The section of the editor the input belongs to.
    /// </summary>
    public string Group =>
        Name switch
        {
            nameof(ScenarioTerms.PurchasePrice) or nameof(ScenarioTerms.InitialFees) => "Price",

            nameof(ScenarioTerms.AnnualKilometres)
            or nameof(ScenarioTerms.IncludedKilometresPerYear)
            or nameof(ScenarioTerms.ExcessKilometrePrice)
            or nameof(ScenarioTerms.UnusedKilometreCredit) => "Mileage",

            _ => "Contract",
        };

    /// <summary>
    /// Whether the input holds an amount in the currency of the evaluation.
    /// </summary>
    public bool Monetary =>
        Name
            is nameof(ScenarioTerms.PurchasePrice)
                or nameof(ScenarioTerms.InitialFees)
                or nameof(ScenarioTerms.DownPayment)
                or nameof(ScenarioTerms.MonthlyPayment)
                or nameof(ScenarioTerms.FinalPayment)
                or nameof(ScenarioTerms.LoanAmount)
                or nameof(ScenarioTerms.ExcessKilometrePrice)
                or nameof(ScenarioTerms.UnusedKilometreCredit);
}

/// <summary>
/// Form of the scenario editor. The numeric terms are posted as text in <see cref="Values"/>,
/// keyed by the name of the <see cref="ScenarioTerms"/> property.
/// </summary>
public sealed class ScenarioForm
{
    // Acquisition modes an input applies to, as the editor script expects them.
    private const string AllModes = "all";
    private const string LeaseModes = "LeaseReturn LeaseBuyout";
    private const string LeaseReturnMode = "LeaseReturn";
    private const string FinancingMode = "Financing";
    private const string ContractModes = "LeaseReturn LeaseBuyout Financing";
    private const string FinalPaymentModes = "LeaseBuyout Financing";

    private const string InvalidScenarioError = "InvalidScenario";

    public string? Name { get; set; }
    public Guid OfferId { get; set; }
    public int RevisionNumber { get; set; }
    public AcquisitionMode Mode { get; set; }
    public Dictionary<string, string?> Values { get; set; } = [];
    public bool CostsReviewed { get; set; }
    public string? Notes { get; set; }

    public static IReadOnlyList<ScenarioField> Fields { get; } =
    [
        new(nameof(ScenarioTerms.PurchasePrice), AllModes),
        new(nameof(ScenarioTerms.InitialFees), AllModes),
        new(nameof(ScenarioTerms.DownPayment), LeaseModes),
        new(nameof(ScenarioTerms.MonthlyPayment), ContractModes),
        new(nameof(ScenarioTerms.TermMonths), ContractModes, "1", "1200"),
        new(nameof(ScenarioTerms.FinalPayment), FinalPaymentModes),
        new(nameof(ScenarioTerms.LoanAmount), FinancingMode),
        new(nameof(ScenarioTerms.LoanInterestPercent), FinancingMode, "0.0001", "100"),
        new(nameof(ScenarioTerms.AnnualKilometres), AllModes, "1", "1000000"),
        new(nameof(ScenarioTerms.IncludedKilometresPerYear), LeaseReturnMode, "1", "1000000"),
        new(nameof(ScenarioTerms.ExcessKilometrePrice), LeaseReturnMode, "0.0001", "10000"),
        new(nameof(ScenarioTerms.UnusedKilometreCredit), LeaseReturnMode, "0.0001", "10000"),
    ];

    public ScenarioInput ToInput() =>
        new(
            Name ?? "",
            OfferId,
            RevisionNumber,
            new()
            {
                Mode = Mode,
                PurchasePrice = Number(Value(nameof(ScenarioTerms.PurchasePrice))),
                InitialFees = Number(Value(nameof(ScenarioTerms.InitialFees))),
                DownPayment = Number(Value(nameof(ScenarioTerms.DownPayment))),
                MonthlyPayment = Number(Value(nameof(ScenarioTerms.MonthlyPayment))),
                TermMonths = Integer(Value(nameof(ScenarioTerms.TermMonths))),
                FinalPayment = Number(Value(nameof(ScenarioTerms.FinalPayment))),
                LoanAmount = Number(Value(nameof(ScenarioTerms.LoanAmount))),
                LoanInterestPercent = Number(Value(nameof(ScenarioTerms.LoanInterestPercent))),
                AnnualKilometres = Integer(Value(nameof(ScenarioTerms.AnnualKilometres))),
                IncludedKilometresPerYear = Integer(
                    Value(nameof(ScenarioTerms.IncludedKilometresPerYear))
                ),
                ExcessKilometrePrice = Number(Value(nameof(ScenarioTerms.ExcessKilometrePrice))),
                UnusedKilometreCredit = Number(Value(nameof(ScenarioTerms.UnusedKilometreCredit))),
                CostsReviewed = CostsReviewed,
            },
            Notes
        );

    public string? Value(string name) => Values.GetValueOrDefault(name);

    public static ScenarioForm From(Scenario scenario)
    {
        var form = new ScenarioForm
        {
            Name = scenario.Name,
            OfferId = scenario.OfferId,
            RevisionNumber = scenario.RevisionNumber,
            Mode = scenario.Terms.Mode,
            CostsReviewed = scenario.Terms.CostsReviewed,
            Notes = scenario.Notes,
        };

        foreach (var field in Fields)
        {
            form.Values[field.Name] = TermText(scenario.Terms, field.Name);
        }

        return form;
    }

    /// <summary>
    /// Prefills a new scenario with what the latest revision of the offer states.
    /// </summary>
    public static ScenarioForm FromOffer(Offer offer)
    {
        var terms = offer.Latest.Terms;
        var pricing = offer.Latest.Pricing;

        var pricedLines = offer
            .Latest.Lines.Where(line => line.PriceKind == OfferPriceKind.Amount)
            .ToList();
        var fees = pricedLines
            .Where(line => line.Kind == OfferLineKind.Fee)
            .Sum(line => line.Amount ?? 0);

        // Purchase pays everything at once; fees count as initial costs. With a contract, the lines that are
        // not part of it are due at handover.
        var isContract = terms.Kind is OfferKind.Leasing or OfferKind.Financing;
        decimal? purchasePrice = null;
        decimal? initialFees = null;

        if (pricing.Known)
        {
            if (isContract)
            {
                purchasePrice = pricing.Financed;
                initialFees = pricing.DueAtHandover;
            }
            else
            {
                purchasePrice = pricing.Total - fees;
                initialFees = fees;
            }
        }

        // The financed part can exceed the total; the form does not accept negative fees.
        var nonNegativeInitialFees = initialFees is < 0 ? 0 : initialFees;

        return new()
        {
            Name = terms.Name,
            OfferId = offer.Id,
            RevisionNumber = offer.Latest.Number,
            Mode = terms.Kind switch
            {
                OfferKind.Leasing => AcquisitionMode.LeaseReturn,
                OfferKind.Financing => AcquisitionMode.Financing,
                _ => AcquisitionMode.Purchase,
            },
            Values = new()
            {
                [nameof(ScenarioTerms.PurchasePrice)] = Format(purchasePrice),
                [nameof(ScenarioTerms.InitialFees)] = Format(nonNegativeInitialFees),
                [nameof(ScenarioTerms.DownPayment)] = Format(terms.DownPayment),
                [nameof(ScenarioTerms.MonthlyPayment)] = Format(terms.MonthlyPayment),
                [nameof(ScenarioTerms.TermMonths)] = terms.TermMonths?.ToString(
                    CultureInfo.InvariantCulture
                ),
                [nameof(ScenarioTerms.FinalPayment)] = Format(terms.ResidualPayment),
                [nameof(ScenarioTerms.LoanInterestPercent)] =
                    terms.Kind == OfferKind.Financing ? Format(terms.AnnualInterestPercent) : null,
                [nameof(ScenarioTerms.IncludedKilometresPerYear)] =
                    terms.IncludedKilometresPerYear?.ToString(CultureInfo.InvariantCulture),
                [nameof(ScenarioTerms.ExcessKilometrePrice)] = Format(terms.ExcessKilometrePrice),
                [nameof(ScenarioTerms.UnusedKilometreCredit)] = Format(terms.UnusedKilometreCredit),
            },
        };
    }

    public static string? Format(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Reads a posted decimal. Empty input means "not stated"; anything unparsable is rejected.
    /// </summary>
    public static decimal? Number(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parsed = decimal.TryParse(
            value.Trim(),
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out var number
        );
        if (!parsed)
        {
            throw new RuleException(InvalidScenarioError);
        }

        return number;
    }

    /// <summary>
    /// Reads a posted whole number. Empty input means "not stated"; anything unparsable is rejected.
    /// </summary>
    public static int? Integer(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parsed = int.TryParse(
            value.Trim(),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var number
        );
        if (!parsed)
        {
            throw new RuleException(InvalidScenarioError);
        }

        return number;
    }

    // The field names are the property names of the terms, so the stored value is read by name.
    private static string? TermText(ScenarioTerms terms, string propertyName) =>
        Convert.ToString(
            typeof(ScenarioTerms).GetProperty(propertyName)!.GetValue(terms),
            CultureInfo.InvariantCulture
        );
}

/// <summary>
/// Form of the residual value editor: the vehicle's value range at one month of the planning period.
/// </summary>
public sealed class ResidualForm
{
    public string? Month { get; set; }
    public string? Low { get; set; }
    public string? Expected { get; set; }
    public string? High { get; set; }
    public string? Notes { get; set; }

    public ResidualInput ToInput() =>
        new(
            ScenarioForm.Integer(Month) ?? throw new RuleException("InvalidResidual"),
            ScenarioForm.Number(Low),
            ScenarioForm.Number(Expected),
            ScenarioForm.Number(High),
            Notes
        );

    public static ResidualForm From(ResidualCheckpoint point) =>
        new()
        {
            Month = point.Month.ToString(CultureInfo.InvariantCulture),
            Low = ScenarioForm.Format(point.Low),
            Expected = ScenarioForm.Format(point.Expected),
            High = ScenarioForm.Format(point.High),
            Notes = point.Notes,
        };
}

/// <summary>
/// Form of the cost adjustment editor: a scenario-specific amount for one planned cost.
/// </summary>
public sealed class AdjustmentForm
{
    public Guid CostId { get; set; }
    public string? Amount { get; set; }
    public string? StartMonth { get; set; } = "1";
    public string? EndMonth { get; set; }
    public string? Notes { get; set; }

    public CostAdjustmentInput ToInput() =>
        new(
            CostId,
            ScenarioForm.Number(Amount),
            ScenarioForm.Integer(StartMonth) ?? throw new RuleException("InvalidCostAdjustment"),
            ScenarioForm.Integer(EndMonth),
            Notes
        );

    public static AdjustmentForm From(ScenarioCostAdjustment item) =>
        new()
        {
            CostId = item.CostId,
            Amount = ScenarioForm.Format(item.Amount),
            StartMonth = item.StartMonth.ToString(CultureInfo.InvariantCulture),
            EndMonth = item.EndMonth?.ToString(CultureInfo.InvariantCulture),
            Notes = item.Notes,
        };
}
