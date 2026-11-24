using Steervo.Core;

namespace Steervo.Calculations;

public sealed record ProjectedCost(CostItem Item, int Occurrences, decimal? Total)
{
    public bool Active => Occurrences > 0;

    public bool Missing => Active && !Total.HasValue;
}

public sealed record CostTotals(decimal Expenses, decimal Income, int MissingItems)
{
    public decimal Net => Expenses - Income;

    public bool Complete => MissingItems == 0;
}

/// <summary>Sums the planned costs of an evaluation over a range of planning months.</summary>
public sealed record CostProjection(IReadOnlyList<ProjectedCost> Items, CostTotals Totals)
{
    private const int MonthsPerYear = 12;

    public static CostProjection Calculate(
        Evaluation evaluation,
        Guid? candidateId = null,
        int fromMonth = 1,
        int? throughMonth = null
    )
    {
        var planning = evaluation.CostPlanning ?? throw new RuleException("CostPlanningRequired");

        if (candidateId.HasValue)
        {
            // Only checks that the candidate exists; an unknown id is a rule violation.
            _ = evaluation.FindCandidate(candidateId.Value);
        }

        var lastMonth = throughMonth ?? planning.Months;

        if (fromMonth < 1 || lastMonth < fromMonth || lastMonth > planning.Months)
        {
            throw new RuleException("InvalidCostPlanning");
        }

        // Costs without a candidate apply to every candidate.
        var items = evaluation
            .Costs.Where(cost => cost.CandidateId is null || cost.CandidateId == candidateId)
            .Select(cost => Project(cost, fromMonth, lastMonth))
            .ToArray();

        var expenses = items
            .Where(projected => projected.Item.Direction == CostDirection.Expense)
            .Sum(projected => projected.Total ?? 0);
        var income = items
            .Where(projected => projected.Item.Direction == CostDirection.Income)
            .Sum(projected => projected.Total ?? 0);
        var missingItems = items.Count(projected => projected.Missing);

        return new(items, new(expenses, income, missingItems));
    }

    private static ProjectedCost Project(CostItem item, int from, int through)
    {
        var firstActiveMonth = Math.Max(from, item.StartMonth);
        var lastActiveMonth = Math.Min(through, item.EndMonth ?? through);
        var occurrences = 0;

        if (firstActiveMonth <= lastActiveMonth)
        {
            occurrences = item.Frequency switch
            {
                CostFrequency.Once => StartsWithin(item, from, through) ? 1 : 0,
                CostFrequency.Monthly or CostFrequency.Included => lastActiveMonth
                    - firstActiveMonth
                    + 1,
                CostFrequency.Yearly => YearlyOccurrences(item, firstActiveMonth, lastActiveMonth),
                _ => throw new RuleException("InvalidCost"),
            };
        }

        // Unknown amounts are never converted to zero for active payments.
        var costsNothing = occurrences == 0 || item.Frequency == CostFrequency.Included;
        decimal? total = costsNothing ? 0 : item.Amount * occurrences;

        return new(item, occurrences, total);
    }

    private static bool StartsWithin(CostItem item, int from, int through)
    {
        return item.StartMonth >= from && item.StartMonth <= through;
    }

    /// <summary>
    /// Counts the yearly due months, starting with the start month, in the active range.
    /// </summary>
    private static int YearlyOccurrences(CostItem item, int firstActiveMonth, int lastActiveMonth)
    {
        // Due months are numbered 0, 1, 2, ... from the start month, one per year.
        var lastDueNumber = (lastActiveMonth - item.StartMonth) / MonthsPerYear;
        var firstDueNumber =
            (firstActiveMonth - item.StartMonth + MonthsPerYear - 1) / MonthsPerYear;

        return lastDueNumber - firstDueNumber + 1;
    }
}
