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
public sealed record CostProjection(IReadOnlyList<ProjectedCost> Items, CostTotals Totals)
{
    public static CostProjection Calculate(Evaluation evaluation, Guid? candidateId = null, int fromMonth = 1, int? throughMonth = null)
    {
        var planning = evaluation.CostPlanning ?? throw new RuleException("CostPlanningRequired");
        if (candidateId.HasValue) _ = evaluation.FindCandidate(candidateId.Value);
        var end = throughMonth ?? planning.Months;
        if (fromMonth < 1 || end < fromMonth || end > planning.Months) throw new RuleException("InvalidCostPlanning");
        var items = evaluation.Costs.Where(c => c.CandidateId is null || c.CandidateId == candidateId)
            .Select(c => Project(c, fromMonth, end)).ToArray();
        return new(items, new(items.Where(c => c.Item.Direction == CostDirection.Expense).Sum(c => c.Total ?? 0),
            items.Where(c => c.Item.Direction == CostDirection.Income).Sum(c => c.Total ?? 0), items.Count(c => c.Missing)));
    }
    private static ProjectedCost Project(CostItem item, int from, int through)
    {
        var first = Math.Max(from, item.StartMonth); var last = Math.Min(through, item.EndMonth ?? through);
        var count = 0;
        if (first <= last)
        {
            count = item.Frequency switch
            {
                CostFrequency.Once => item.StartMonth >= from && item.StartMonth <= through ? 1 : 0,
                CostFrequency.Monthly or CostFrequency.Included => last - first + 1,
                CostFrequency.Yearly => (last - item.StartMonth) / 12 - (first - item.StartMonth + 11) / 12 + 1,
                _ => throw new RuleException("InvalidCost")
            };
        }
        // Unknown amounts are never converted to zero for active payments.
        decimal? total = count == 0 || item.Frequency == CostFrequency.Included ? 0 : item.Amount * count;
        return new(item, count, total);
    }
}
