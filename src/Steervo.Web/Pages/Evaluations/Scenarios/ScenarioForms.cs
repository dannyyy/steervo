using System.Globalization;
using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations.Scenarios;

public sealed record ScenarioField(string Name, string Modes, string Step = "0.01", string Max = "1000000000000")
{
    public string Group => Name is "PurchasePrice" or "InitialFees" ? "Price" : Name is "AnnualKilometres" or "IncludedKilometresPerYear" or "ExcessKilometrePrice" or "UnusedKilometreCredit" ? "Mileage" : "Contract";
    public bool Monetary => Name is "PurchasePrice" or "InitialFees" or "DownPayment" or "MonthlyPayment" or "FinalPayment" or "LoanAmount" or "ExcessKilometrePrice" or "UnusedKilometreCredit";
}
public sealed class ScenarioForm
{
    public string? Name { get; set; }
    public Guid OfferId { get; set; }
    public int RevisionNumber { get; set; }
    public AcquisitionMode Mode { get; set; }
    public Dictionary<string, string?> Values { get; set; } = [];
    public bool CostsReviewed { get; set; }
    public string? Notes { get; set; }
    public static IReadOnlyList<ScenarioField> Fields { get; } = [
        new("PurchasePrice", "all"), new("InitialFees", "all"),
        new("DownPayment", "LeaseReturn LeaseBuyout"), new("MonthlyPayment", "LeaseReturn LeaseBuyout Financing"),
        new("TermMonths", "LeaseReturn LeaseBuyout Financing", "1", "1200"), new("FinalPayment", "LeaseBuyout Financing"),
        new("LoanAmount", "Financing"), new("LoanInterestPercent", "Financing", "0.0001", "100"),
        new("AnnualKilometres", "all", "1", "1000000"), new("IncludedKilometresPerYear", "LeaseReturn", "1", "1000000"),
        new("ExcessKilometrePrice", "LeaseReturn", "0.0001", "10000"), new("UnusedKilometreCredit", "LeaseReturn", "0.0001", "10000")];
    public ScenarioInput ToInput() => new(Name ?? "", OfferId, RevisionNumber, new()
    {
        Mode = Mode, PurchasePrice = Number(Value("PurchasePrice")), InitialFees = Number(Value("InitialFees")), DownPayment = Number(Value("DownPayment")),
        MonthlyPayment = Number(Value("MonthlyPayment")), TermMonths = Integer(Value("TermMonths")), FinalPayment = Number(Value("FinalPayment")),
        LoanAmount = Number(Value("LoanAmount")), LoanInterestPercent = Number(Value("LoanInterestPercent")), AnnualKilometres = Integer(Value("AnnualKilometres")),
        IncludedKilometresPerYear = Integer(Value("IncludedKilometresPerYear")), ExcessKilometrePrice = Number(Value("ExcessKilometrePrice")),
        UnusedKilometreCredit = Number(Value("UnusedKilometreCredit")), CostsReviewed = CostsReviewed
    }, Notes);
    public string? Value(string name) => Values.GetValueOrDefault(name);
    public static ScenarioForm From(Scenario scenario)
    {
        var form = new ScenarioForm { Name = scenario.Name, OfferId = scenario.OfferId, RevisionNumber = scenario.RevisionNumber,
            Mode = scenario.Terms.Mode, CostsReviewed = scenario.Terms.CostsReviewed, Notes = scenario.Notes };
        foreach (var field in Fields) form.Values[field.Name] = Convert.ToString(typeof(ScenarioTerms).GetProperty(field.Name)!.GetValue(scenario.Terms), CultureInfo.InvariantCulture);
        return form;
    }
    public static ScenarioForm FromOffer(Offer offer)
    {
        var t = offer.Latest.Terms;
        return new() { Name = t.Name, OfferId = offer.Id, RevisionNumber = offer.Latest.Number,
            Mode = t.Kind switch { OfferKind.Leasing => AcquisitionMode.LeaseReturn, OfferKind.Financing => AcquisitionMode.Financing, _ => AcquisitionMode.Purchase },
            Values = new() { ["DownPayment"] = Format(t.DownPayment), ["MonthlyPayment"] = Format(t.MonthlyPayment),
                ["TermMonths"] = t.TermMonths?.ToString(CultureInfo.InvariantCulture), ["FinalPayment"] = Format(t.ResidualPayment),
                ["LoanInterestPercent"] = t.Kind == OfferKind.Financing ? Format(t.AnnualInterestPercent) : null,
                ["IncludedKilometresPerYear"] = t.IncludedKilometresPerYear?.ToString(CultureInfo.InvariantCulture),
                ["ExcessKilometrePrice"] = Format(t.ExcessKilometrePrice), ["UnusedKilometreCredit"] = Format(t.UnusedKilometreCredit) } };
    }
    public static string? Format(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);
    public static decimal? Number(string? value) => string.IsNullOrWhiteSpace(value) ? null
        : decimal.TryParse(value.Trim(), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number) ? number : throw new RuleException("InvalidScenario");
    public static int? Integer(string? value) => string.IsNullOrWhiteSpace(value) ? null
        : int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : throw new RuleException("InvalidScenario");
}
public sealed class ResidualForm
{
    public string? Month { get; set; }
    public string? Low { get; set; }
    public string? Expected { get; set; }
    public string? High { get; set; }
    public string? Notes { get; set; }
    public ResidualInput ToInput() => new(ScenarioForm.Integer(Month) ?? throw new RuleException("InvalidResidual"), ScenarioForm.Number(Low), ScenarioForm.Number(Expected), ScenarioForm.Number(High), Notes);
    public static ResidualForm From(ResidualCheckpoint point) => new() { Month = point.Month.ToString(CultureInfo.InvariantCulture), Low = ScenarioForm.Format(point.Low), Expected = ScenarioForm.Format(point.Expected), High = ScenarioForm.Format(point.High), Notes = point.Notes };
}
public sealed class AdjustmentForm
{
    public Guid CostId { get; set; }
    public string? Amount { get; set; }
    public string? StartMonth { get; set; } = "1";
    public string? EndMonth { get; set; }
    public string? Notes { get; set; }
    public CostAdjustmentInput ToInput() => new(CostId, ScenarioForm.Number(Amount), ScenarioForm.Integer(StartMonth) ?? throw new RuleException("InvalidCostAdjustment"), ScenarioForm.Integer(EndMonth), Notes);
    public static AdjustmentForm From(ScenarioCostAdjustment item) => new() { CostId = item.CostId, Amount = ScenarioForm.Format(item.Amount), StartMonth = item.StartMonth.ToString(CultureInfo.InvariantCulture), EndMonth = item.EndMonth?.ToString(CultureInfo.InvariantCulture), Notes = item.Notes };
}
