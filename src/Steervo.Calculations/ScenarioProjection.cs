using Steervo.Core;

namespace Steervo.Calculations;

/// <summary>
/// Projects one scenario month by month up to a horizon: books every payment, tracks the loan
/// balance and collects what is missing for a complete result.
/// </summary>
internal sealed class ScenarioProjection
{
    private const string MileageComponent = "CalculationMileage";
    private const string CreditInconsistentIssue = "ScenarioCreditInconsistent";

    private readonly Evaluation evaluation;
    private readonly Scenario scenario;
    private readonly ScenarioTerms terms;
    private readonly int horizon;

    private readonly decimal referenceCapital;
    private readonly decimal monthlyFactor;

    // growthFactors[n] is the value of one unit of money after n months at the assumed return.
    private readonly decimal[] growthFactors;

    private readonly List<CalculationIssue> issues = [];
    private readonly Dictionary<string, CalculationComponent> components = [];

    // Payments per month, index 0 unused. Start-of-month payments miss that month's return,
    // end-of-month payments do not.
    private readonly decimal[] startExpenses;
    private readonly decimal[] startIncome;
    private readonly decimal[] endExpenses;
    private readonly decimal[] endIncome;
    private readonly decimal[] loanDebt;

    private ScenarioProjection(Evaluation evaluation, Scenario scenario, int horizon)
    {
        this.evaluation = evaluation;
        this.scenario = scenario;
        this.horizon = horizon;
        terms = scenario.Terms;
        referenceCapital = ScenarioCalculation.ReferenceCapital(evaluation);

        if (evaluation.CalculationSettings is null)
        {
            issues.Add(new("CalculationReturnMissing"));
        }

        if (!terms.CostsReviewed)
        {
            issues.Add(new("ScenarioCostsUnreviewed"));
        }

        var annualReturnPercent = evaluation.CalculationSettings?.AnnualReturnPercent ?? 0;
        monthlyFactor = ScenarioCalculation.MonthlyFactor(annualReturnPercent);
        growthFactors = CompoundFactors(monthlyFactor, horizon);

        startExpenses = new decimal[horizon + 1];
        startIncome = new decimal[horizon + 1];
        endExpenses = new decimal[horizon + 1];
        endIncome = new decimal[horizon + 1];
        loanDebt = new decimal[horizon + 1];
    }

    public static ScenarioResult Run(Evaluation evaluation, Scenario scenario, int horizon)
    {
        var projection = new ScenarioProjection(evaluation, scenario, horizon);

        projection.BookAcquisition();
        projection.BookRunningCosts();

        return projection.BuildResult();
    }

    private static decimal[] CompoundFactors(decimal monthlyFactor, int months)
    {
        var factors = new decimal[months + 1];
        factors[0] = 1;

        for (var month = 1; month <= months; month++)
        {
            factors[month] = factors[month - 1] * monthlyFactor;
        }

        return factors;
    }

    private void BookAcquisition()
    {
        Book(
            1,
            Required(terms.InitialFees, "InitialFees"),
            "CalculationInitialFees",
            known: terms.InitialFees.HasValue
        );

        var isLease = terms.Mode is AcquisitionMode.LeaseReturn or AcquisitionMode.LeaseBuyout;
        var isFinanced = terms.Mode == AcquisitionMode.Financing;

        if (isLease || isFinanced)
        {
            if (!terms.TermMonths.HasValue)
            {
                AddIssue("ScenarioMissingTermMonths");
            }

            _ = Required(terms.MonthlyPayment, "MonthlyPayment");
        }

        if (terms.Mode == AcquisitionMode.Purchase)
        {
            Book(
                1,
                Required(terms.PurchasePrice, "PurchasePrice"),
                "CalculationPurchase",
                known: terms.PurchasePrice.HasValue
            );
        }

        if (isLease)
        {
            BookLease();
        }

        if (isFinanced)
        {
            BookFinancing();
        }
    }

    private void BookLease()
    {
        var term = terms.TermMonths;

        Book(
            1,
            Required(terms.DownPayment, "DownPayment"),
            "CalculationDownPayment",
            known: terms.DownPayment.HasValue
        );

        var lastPaidMonth = Math.Min(term ?? 0, horizon);

        for (var month = 1; month <= lastPaidMonth; month++)
        {
            Book(
                month,
                terms.MonthlyPayment ?? 0,
                "CalculationLeasePayments",
                known: terms.MonthlyPayment.HasValue
            );
        }

        if (term.HasValue)
        {
            // A lease cannot be judged before its term ends, and a returned car is gone afterwards.
            var endsBeforeTerm = horizon < term;
            var returnedAtAnotherMonth =
                terms.Mode == AcquisitionMode.LeaseReturn && horizon != term;

            if (endsBeforeTerm || returnedAtAnotherMonth)
            {
                AddIssue("ScenarioLeaseHorizon");
            }
        }

        if (terms.Mode == AcquisitionMode.LeaseBuyout)
        {
            BookBuyout(term);
        }
        else if (term.HasValue && horizon == term)
        {
            BookMileageSettlement(term.Value);
        }
    }

    private void BookBuyout(int? term)
    {
        var buyout = Required(terms.FinalPayment, "FinalPayment");

        if (term.HasValue)
        {
            Book(
                term.Value,
                buyout,
                "CalculationBuyout",
                atEnd: true,
                known: terms.FinalPayment.HasValue
            );
        }
    }

    /// <summary>Settles driven against included kilometres when a leased car is returned.</summary>
    private void BookMileageSettlement(int term)
    {
        if (!terms.AnnualKilometres.HasValue)
        {
            AddIssue("ScenarioMissingAnnualKilometres");
        }

        if (!terms.IncludedKilometresPerYear.HasValue)
        {
            AddIssue("ScenarioMissingIncludedKilometresPerYear");
        }

        if (!terms.AnnualKilometres.HasValue || !terms.IncludedKilometresPerYear.HasValue)
        {
            return;
        }

        // Positive: kilometres driven beyond the allowance; negative: unused allowance.
        var difference =
            (terms.AnnualKilometres.Value - terms.IncludedKilometresPerYear.Value) * term / 12m;

        if (difference > 0)
        {
            var excessCharge = decimal.Round(
                difference * Required(terms.ExcessKilometrePrice, "ExcessKilometrePrice"),
                2,
                MidpointRounding.AwayFromZero
            );

            Book(
                horizon,
                excessCharge,
                MileageComponent,
                atEnd: true,
                known: terms.ExcessKilometrePrice.HasValue
            );
        }

        if (difference < 0)
        {
            // The difference is negative, so the credit is booked as income.
            var unusedCredit = decimal.Round(
                difference * Required(terms.UnusedKilometreCredit, "UnusedKilometreCredit"),
                2,
                MidpointRounding.AwayFromZero
            );

            Book(
                horizon,
                unusedCredit,
                MileageComponent,
                atEnd: true,
                known: terms.UnusedKilometreCredit.HasValue
            );
        }
    }

    private void BookFinancing()
    {
        var price = Required(terms.PurchasePrice, "PurchasePrice");
        var principal = Required(terms.LoanAmount, "LoanAmount");
        // Annual percent to monthly fraction: / 100 / 12.
        var monthlyRate = Required(terms.LoanInterestPercent, "LoanInterestPercent") / 1200m;
        var balloon = Required(terms.FinalPayment, "FinalPayment");

        var equityKnown = terms.PurchasePrice.HasValue && terms.LoanAmount.HasValue;

        Book(1, equityKnown ? price - principal : 0, "CalculationEquity", known: equityKnown);

        var remainingDebt = RepayLoan(principal, monthlyRate, balloon);

        if (terms.TermMonths.HasValue && remainingDebt != 0)
        {
            AddIssue(CreditInconsistentIssue);
        }
    }

    /// <summary>
    /// Books the instalments and the balloon over the whole term and records the balance for
    /// every month inside the horizon. Returns the balance left after the last booked month.
    /// </summary>
    private decimal RepayLoan(decimal principal, decimal monthlyRate, decimal balloon)
    {
        var term = terms.TermMonths;
        var debt = principal;

        for (var month = 1; month <= (term ?? 0); month++)
        {
            var interest = decimal.Round(debt * monthlyRate, 2, MidpointRounding.AwayFromZero);
            debt += interest - (terms.MonthlyPayment ?? 0);

            Book(
                month,
                terms.MonthlyPayment ?? 0,
                "CalculationCreditPayments",
                atEnd: true,
                known: terms.MonthlyPayment.HasValue
            );

            if (month == term)
            {
                debt -= balloon;

                Book(
                    month,
                    balloon,
                    "CalculationBalloon",
                    atEnd: true,
                    known: terms.FinalPayment.HasValue
                );
            }

            if (debt < 0)
            {
                AddIssue(CreditInconsistentIssue);
                break;
            }

            if (month <= horizon)
            {
                loanDebt[month] = debt;
            }
        }

        return debt;
    }

    private void BookRunningCosts()
    {
        var candidateId = evaluation.FindOffer(scenario.OfferId).CandidateId;
        var costs = evaluation
            .Costs.Where(c => c.CandidateId is null || c.CandidateId == candidateId)
            .ToArray();
        var adjustments = scenario.CostAdjustments.ToDictionary(a => a.CostId);
        var coveredMonths = ScenarioCalculation
            .CoveredCosts(evaluation, scenario)
            .GroupBy(covered => covered.Category)
            .ToDictionary(group => group.Key, group => group.Max(covered => covered.Months));

        foreach (var cost in costs)
        {
            adjustments.TryGetValue(cost.Id, out var adjustment);

            BookCost(cost, adjustment, coveredMonths);
        }
    }

    private void BookCost(
        CostItem cost,
        ScenarioCostAdjustment? adjustment,
        Dictionary<CostCategory, int> coveredMonths
    )
    {
        var lastMonth = Math.Min(horizon, cost.EndMonth ?? horizon);
        var sign = cost.Direction == CostDirection.Expense ? 1 : -1;

        for (var month = cost.StartMonth; month <= lastMonth; month++)
        {
            if (cost.Frequency == CostFrequency.Included)
            {
                break;
            }

            if (cost.Frequency == CostFrequency.Once && month != cost.StartMonth)
            {
                break;
            }

            var isYearlyDueMonth = (month - cost.StartMonth) % 12 == 0;

            if (cost.Frequency == CostFrequency.Yearly && !isYearlyDueMonth)
            {
                continue;
            }

            var amount = AmountDue(cost, month, adjustment, coveredMonths);

            if (!amount.HasValue)
            {
                AddIssue("ScenarioMissingCost", cost.Name);
            }

            Book(
                month,
                (amount ?? 0) * sign,
                "CalculationAdditionalCost",
                cost.Name,
                key: cost.Id.ToString(),
                known: amount.HasValue
            );
        }
    }

    private decimal? AmountDue(
        CostItem cost,
        int month,
        ScenarioCostAdjustment? adjustment,
        Dictionary<CostCategory, int> coveredMonths
    )
    {
        // An explicit adjustment of this scenario wins, also outside its own period: the offer's
        // free benefits then no longer apply to this cost.
        if (adjustment is not null)
        {
            var adjustmentApplies =
                month >= adjustment.StartMonth && month <= (adjustment.EndMonth ?? horizon);

            return adjustmentApplies ? adjustment.Amount : cost.Amount;
        }

        // What the offer includes for free costs nothing while it lasts.
        var coveredByOffer =
            cost.Direction == CostDirection.Expense
            && coveredMonths.TryGetValue(cost.Category, out var months)
            && month <= months;

        return coveredByOffer ? 0 : cost.Amount;
    }

    private ScenarioResult BuildResult()
    {
        var rows = new List<ScenarioMonth>();
        var account = referenceCapital;
        var netPayments = 0m;

        for (var month = 1; month <= horizon; month++)
        {
            account -= startExpenses[month] - startIncome[month];
            var interest = account * (monthlyFactor - 1);
            account += interest - endExpenses[month] + endIncome[month];
            netPayments +=
                startExpenses[month] - startIncome[month] + endExpenses[month] - endIncome[month];

            rows.Add(
                new(
                    month,
                    startExpenses[month],
                    startIncome[month],
                    endExpenses[month],
                    endIncome[month],
                    interest,
                    account,
                    loanDebt[month]
                )
            );
        }

        var futurePayments = components.Values.Sum(c => c.FuturePayments);
        var debtAtEnd = loanDebt[horizon];
        var checkpoint = scenario.Residuals.SingleOrDefault(r => r.Month == horizon);

        // A returned lease car leaves no residual value, whatever was estimated.
        var returned = terms.Mode == AcquisitionMode.LeaseReturn;
        decimal? lowResidual = returned ? 0 : checkpoint?.Low;
        decimal? expectedResidual = returned ? 0 : checkpoint?.Expected;
        decimal? highResidual = returned ? 0 : checkpoint?.High;

        decimal? CostWith(decimal? residual)
        {
            if (issues.Count != 0 || !residual.HasValue)
            {
                return null;
            }

            return futurePayments + debtAtEnd - residual.Value;
        }

        var expectedCost = CostWith(expectedResidual);

        return new(
            Month: horizon,
            ReferenceCapital: referenceCapital,
            NetPayments: netPayments,
            OpportunityCost: futurePayments - netPayments,
            Debt: debtAtEnd,
            Account: issues.Count == 0 ? account : null,
            PessimisticCost: CostWith(lowResidual),
            ExpectedCost: expectedCost,
            OptimisticCost: CostWith(highResidual),
            ExpectedWealth: expectedCost.HasValue
                ? referenceCapital * growthFactors[horizon] - expectedCost
                : null,
            ExpectedResidual: expectedResidual,
            Issues: issues,
            Months: rows,
            Components: components.Values.ToArray()
        );
    }

    /// <summary>
    /// Books one payment: a negative amount is income. Payments after the horizon are ignored.
    /// Payments sharing a key (default: the code) add up to one component of the result.
    /// </summary>
    private void Book(
        int month,
        decimal amount,
        string code,
        string? name = null,
        bool atEnd = false,
        string? key = null,
        bool known = true
    )
    {
        if (month > horizon)
        {
            return;
        }

        if (atEnd)
        {
            if (amount >= 0)
            {
                endExpenses[month] += amount;
            }
            else
            {
                endIncome[month] -= amount;
            }
        }
        else
        {
            if (amount >= 0)
            {
                startExpenses[month] += amount;
            }
            else
            {
                startIncome[month] -= amount;
            }
        }

        // A start-of-month payment would have earned the return of its own month as well.
        var monthsInvested = horizon - month + (atEnd ? 0 : 1);
        var componentKey = key ?? code;
        var current = components.GetValueOrDefault(componentKey) ?? new(code, name, 0, 0);

        components[componentKey] = current with
        {
            Complete = current.Complete && known,
            NetPayments = current.NetPayments + amount,
            FuturePayments = current.FuturePayments + amount * growthFactors[monthsInvested],
        };
    }

    private void AddIssue(string code, string? detail = null)
    {
        var issue = new CalculationIssue(code, detail);

        if (!issues.Contains(issue))
        {
            issues.Add(issue);
        }
    }

    /// <summary>Returns the value or zero, noting the missing field as an issue.</summary>
    private decimal Required(decimal? value, string field)
    {
        if (!value.HasValue)
        {
            AddIssue("ScenarioMissing" + field);
        }

        return value ?? 0;
    }
}
