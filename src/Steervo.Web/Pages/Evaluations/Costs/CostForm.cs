using System.Globalization;
using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations.Costs;

public sealed class CostForm
{
    public string Name { get; set; } = "";
    public Guid? CandidateId { get; set; }
    public CostCategory Category { get; set; } = CostCategory.Other;
    public CostFrequency Frequency { get; set; } = CostFrequency.Monthly;
    public CostDirection Direction { get; set; }
    public string? Amount { get; set; }
    public string StartMonth { get; set; } = "1";
    public string? EndMonth { get; set; }
    public string? Notes { get; set; }
    public CostInput ToInput() => new(Name, Category, Frequency, ParseAmount(Amount), ParseMonth(StartMonth) ?? throw new RuleException("InvalidCost"), ParseMonth(EndMonth), CandidateId, Direction, Notes);
    public static CostForm From(CostItem item) => new() {
        Name = item.Name, CandidateId = item.CandidateId, Category = item.Category, Frequency = item.Frequency, Direction = item.Direction,
        Amount = item.Amount?.ToString(CultureInfo.InvariantCulture), StartMonth = item.StartMonth.ToString(CultureInfo.InvariantCulture),
        EndMonth = item.EndMonth?.ToString(CultureInfo.InvariantCulture), Notes = item.Notes
    };
    public static int? ParseMonth(string? value) => string.IsNullOrWhiteSpace(value) ? null
        : int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : throw new RuleException("InvalidCost");
    private static decimal? ParseAmount(string? value) => string.IsNullOrWhiteSpace(value) ? null
        : decimal.TryParse(value.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) ? number : throw new RuleException("InvalidCost");
}
public sealed class CostPlanningForm
{
    public string Start { get; set; } = "";
    public string Months { get; set; } = "";
    public DateOnly ParseStart() => DateOnly.TryParseExact(Start + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
        ? start : throw new RuleException("InvalidCostPlanning");
    public int ParseMonths() => CostForm.ParseMonth(Months) ?? throw new RuleException("InvalidCostPlanning");
    public static CostPlanningForm From(CostPlanning? plan) => plan is null ? new() : new() { Start = plan.Start.ToString("yyyy-MM", CultureInfo.InvariantCulture), Months = plan.Months.ToString(CultureInfo.InvariantCulture) };
}
