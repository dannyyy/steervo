using Steervo.Core;

namespace Steervo.Calculations;

public sealed record CalculationIssue(string Code, string? Detail = null);
public sealed record ScenarioMonth(int Month, decimal StartExpenses, decimal StartIncome, decimal EndExpenses, decimal EndIncome,
    decimal Interest, decimal Account, decimal Debt)
{
    public decimal NetPayments => StartExpenses + EndExpenses - StartIncome - EndIncome;
}
public sealed record CalculationComponent(string Code, string? Name, decimal NetPayments, decimal FuturePayments, bool Complete = true)
{
    public decimal OpportunityCost => FuturePayments - NetPayments;
}
public sealed record ScenarioResult(int Month, decimal ReferenceCapital, decimal NetPayments, decimal OpportunityCost, decimal Debt,
    decimal? Account, decimal? PessimisticCost, decimal? ExpectedCost, decimal? OptimisticCost,
    decimal? ExpectedWealth, decimal? ExpectedResidual, IReadOnlyList<CalculationIssue> Issues,
    IReadOnlyList<ScenarioMonth> Months, IReadOnlyList<CalculationComponent> Components)
{
    public bool Complete => ExpectedCost.HasValue;
}

public static class ScenarioCalculation
{
    // A common reference amount makes account balances comparable. It is not a budget.
    public static decimal ReferenceCapital(Evaluation evaluation) => evaluation.Scenarios.Select(s => s.Terms.PurchasePrice ?? 0).DefaultIfEmpty().Max();
    public static ScenarioResult Calculate(Evaluation evaluation, Guid scenarioId, int month)
    {
        var scenario = evaluation.FindScenario(scenarioId);
        var plan = evaluation.CostPlanning ?? throw new RuleException("ScenarioPlanningRequired");
        if (month < 1 || month > plan.Months) throw new RuleException("InvalidCostPlanning");
        try { return Project(evaluation, scenario, month); }
        catch (OverflowException)
        {
            return new(month, ReferenceCapital(evaluation), 0, 0, 0, null, null, null, null, null, null,
                [new("CalculationOverflow")], [], []);
        }
    }
    private static ScenarioResult Project(Evaluation evaluation, Scenario scenario, int horizon)
    {
        var terms = scenario.Terms; var issues = new List<CalculationIssue>();
        var reference = ReferenceCapital(evaluation);
        if (evaluation.CalculationSettings is null) issues.Add(new("CalculationReturnMissing"));
        if (!terms.CostsReviewed) issues.Add(new("ScenarioCostsUnreviewed"));
        var factor = MonthlyFactor(evaluation.CalculationSettings?.AnnualReturnPercent ?? 0);
        var factors = new decimal[horizon + 1]; factors[0] = 1;
        for (var i = 1; i <= horizon; i++) factors[i] = factors[i - 1] * factor;
        var expenses = new decimal[horizon + 1]; var income = new decimal[horizon + 1];
        var endExpenses = new decimal[horizon + 1]; var endIncome = new decimal[horizon + 1];
        var components = new Dictionary<string, CalculationComponent>();
        void Issue(string code, string? detail = null)
        {
            var issue = new CalculationIssue(code, detail); if (!issues.Contains(issue)) issues.Add(issue);
        }
        decimal Required(decimal? value, string field)
        {
            if (!value.HasValue) Issue("ScenarioMissing" + field);
            return value ?? 0;
        }
        void Pay(int month, decimal amount, string code, string? name = null, bool atEnd = false, string? key = null, bool known = true)
        {
            if (month > horizon) return;
            if (atEnd) { if (amount >= 0) endExpenses[month] += amount; else endIncome[month] -= amount; }
            else { if (amount >= 0) expenses[month] += amount; else income[month] -= amount; }
            var id = key ?? code; var current = components.GetValueOrDefault(id) ?? new(code, name, 0, 0);
            components[id] = current with { Complete = current.Complete && known, NetPayments = current.NetPayments + amount,
                FuturePayments = current.FuturePayments + amount * factors[horizon - month + (atEnd ? 0 : 1)] };
        }
        Pay(1, Required(terms.InitialFees, "InitialFees"), "CalculationInitialFees", known: terms.InitialFees.HasValue);
        var loanDebt = new decimal[horizon + 1];
        var lease = terms.Mode is AcquisitionMode.LeaseReturn or AcquisitionMode.LeaseBuyout;
        var financed = terms.Mode == AcquisitionMode.Financing;
        var term = terms.TermMonths;
        if (lease || financed)
        {
            if (!term.HasValue) Issue("ScenarioMissingTermMonths");
            _ = Required(terms.MonthlyPayment, "MonthlyPayment");
        }
        if (terms.Mode == AcquisitionMode.Purchase) Pay(1, Required(terms.PurchasePrice, "PurchasePrice"), "CalculationPurchase", known: terms.PurchasePrice.HasValue);
        if (lease)
        {
            Pay(1, Required(terms.DownPayment, "DownPayment"), "CalculationDownPayment", known: terms.DownPayment.HasValue);
            for (var month = 1; month <= Math.Min(term ?? 0, horizon); month++) Pay(month, terms.MonthlyPayment ?? 0, "CalculationLeasePayments", known: terms.MonthlyPayment.HasValue);
            if (term.HasValue && (horizon < term || (terms.Mode == AcquisitionMode.LeaseReturn && horizon != term))) Issue("ScenarioLeaseHorizon");
            if (terms.Mode == AcquisitionMode.LeaseBuyout)
            {
                var buyout = Required(terms.FinalPayment, "FinalPayment");
                if (term.HasValue) Pay(term.Value, buyout, "CalculationBuyout", atEnd: true, known: terms.FinalPayment.HasValue);
            }
            else if (term.HasValue && horizon == term)
            {
                if (!terms.AnnualKilometres.HasValue) Issue("ScenarioMissingAnnualKilometres");
                if (!terms.IncludedKilometresPerYear.HasValue) Issue("ScenarioMissingIncludedKilometresPerYear");
                if (terms.AnnualKilometres.HasValue && terms.IncludedKilometresPerYear.HasValue)
                {
                    var difference = (terms.AnnualKilometres.Value - terms.IncludedKilometresPerYear.Value) * term.Value / 12m;
                    if (difference > 0) Pay(horizon, decimal.Round(difference * Required(terms.ExcessKilometrePrice, "ExcessKilometrePrice"), 2, MidpointRounding.AwayFromZero), "CalculationMileage", atEnd: true, known: difference > 0 ? terms.ExcessKilometrePrice.HasValue : terms.UnusedKilometreCredit.HasValue);
                    if (difference < 0) Pay(horizon, decimal.Round(difference * Required(terms.UnusedKilometreCredit, "UnusedKilometreCredit"), 2, MidpointRounding.AwayFromZero), "CalculationMileage", atEnd: true, known: difference > 0 ? terms.ExcessKilometrePrice.HasValue : terms.UnusedKilometreCredit.HasValue);
                }
            }
        }
        if (financed)
        {
            var price = Required(terms.PurchasePrice, "PurchasePrice"); var principal = Required(terms.LoanAmount, "LoanAmount");
            var rate = Required(terms.LoanInterestPercent, "LoanInterestPercent") / 1200m;
            var balloon = Required(terms.FinalPayment, "FinalPayment");
            Pay(1, terms.PurchasePrice.HasValue && terms.LoanAmount.HasValue ? price - principal : 0, "CalculationEquity", known: terms.PurchasePrice.HasValue && terms.LoanAmount.HasValue);
            var debt = principal;
            for (var month = 1; month <= (term ?? 0); month++)
            {
                var interest = decimal.Round(debt * rate, 2, MidpointRounding.AwayFromZero);
                debt += interest - (terms.MonthlyPayment ?? 0);
                Pay(month, terms.MonthlyPayment ?? 0, "CalculationCreditPayments", atEnd: true, known: terms.MonthlyPayment.HasValue);
                if (month == term) { debt -= balloon; Pay(month, balloon, "CalculationBalloon", atEnd: true, known: terms.FinalPayment.HasValue); }
                if (debt < 0) { Issue("ScenarioCreditInconsistent"); break; }
                if (month <= horizon) loanDebt[month] = debt;
            }
            if (term.HasValue && debt != 0) Issue("ScenarioCreditInconsistent");
        }
        var candidateId = evaluation.FindOffer(scenario.OfferId).CandidateId;
        var costs = evaluation.Costs.Where(c => c.CandidateId is null || c.CandidateId == candidateId).ToArray();
        var adjustments = scenario.CostAdjustments.ToDictionary(a => a.CostId);
        foreach (var cost in costs)
        {
            for (var month = cost.StartMonth; month <= Math.Min(horizon, cost.EndMonth ?? horizon); month++)
            {
                if (cost.Frequency == CostFrequency.Included) break;
                if (cost.Frequency == CostFrequency.Once && month != cost.StartMonth) break;
                if (cost.Frequency == CostFrequency.Yearly && (month - cost.StartMonth) % 12 != 0) continue;
                var amount = cost.Amount;
                if (adjustments.TryGetValue(cost.Id, out var adjustment) && month >= adjustment.StartMonth && month <= (adjustment.EndMonth ?? horizon)) amount = adjustment.Amount;
                if (!amount.HasValue) Issue("ScenarioMissingCost", cost.Name);
                Pay(month, (amount ?? 0) * (cost.Direction == CostDirection.Expense ? 1 : -1), "CalculationAdditionalCost", cost.Name, key: cost.Id.ToString(), known: amount.HasValue);
            }
        }
        var rows = new List<ScenarioMonth>(); var account = reference; var net = 0m;
        for (var month = 1; month <= horizon; month++)
        {
            account -= expenses[month] - income[month];
            var interest = account * (factor - 1);
            account += interest - endExpenses[month] + endIncome[month];
            net += expenses[month] - income[month] + endExpenses[month] - endIncome[month];
            rows.Add(new(month, expenses[month], income[month], endExpenses[month], endIncome[month], interest, account, loanDebt[month]));
        }
        var future = components.Values.Sum(c => c.FuturePayments); var debtAtEnd = loanDebt[horizon];
        var checkpoint = scenario.Residuals.SingleOrDefault(r => r.Month == horizon);
        var returned = terms.Mode == AcquisitionMode.LeaseReturn;
        decimal? low = returned ? 0 : checkpoint?.Low, expected = returned ? 0 : checkpoint?.Expected, high = returned ? 0 : checkpoint?.High;
        decimal? Cost(decimal? residual) => issues.Count == 0 && residual.HasValue ? future + debtAtEnd - residual.Value : null;
        var expectedCost = Cost(expected);
        return new(horizon, reference, net, future - net, debtAtEnd, issues.Count == 0 ? account : null,
            Cost(low), expectedCost, Cost(high), expectedCost.HasValue ? reference * factors[horizon] - expectedCost : null,
            expected, issues, rows, components.Values.ToArray());
    }
    public static decimal MonthlyFactor(decimal annualPercent)
    {
        if (annualPercent is < -99 or > 100) throw new RuleException("InvalidReturn");
        var annual = 1 + annualPercent / 100;
        if (annual == 1) return 1;
        var value = (decimal)Math.Pow((double)annual, 1d / 12);
        // Newton iteration refines the seed using decimal arithmetic; no monthly money rounding.
        for (var i = 0; i < 20; i++)
        {
            var power = 1m; for (var j = 0; j < 11; j++) power *= value;
            var next = (11 * value + annual / power) / 12;
            if (Math.Abs(next - value) < 0.00000000000000000000000001m) return next;
            value = next;
        }
        return value;
    }
}
