using System.Globalization;
using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations.Costs;

/// <summary>
/// The fields of a cost item as they are posted. Numbers arrive as text so that an invalid
/// entry is reported as a cost error instead of a model binding failure.
/// </summary>
public sealed class CostForm
{
    private const string InvalidCostError = "InvalidCost";

    public string Name { get; set; } = "";

    public Guid? CandidateId { get; set; }

    public CostCategory Category { get; set; } = CostCategory.Other;

    public CostFrequency Frequency { get; set; } = CostFrequency.Monthly;

    public CostDirection Direction { get; set; }

    public string? Amount { get; set; }

    public string StartMonth { get; set; } = "1";

    public string? EndMonth { get; set; }

    public string? Notes { get; set; }

    public CostInput ToInput()
    {
        return new(
            Name,
            Category,
            Frequency,
            ParseAmount(Amount),
            ParseMonth(StartMonth) ?? throw new RuleException(InvalidCostError),
            ParseMonth(EndMonth),
            CandidateId,
            Direction,
            Notes
        );
    }

    public static CostForm From(CostItem item)
    {
        return new()
        {
            Name = item.Name,
            CandidateId = item.CandidateId,
            Category = item.Category,
            Frequency = item.Frequency,
            Direction = item.Direction,
            Amount = item.Amount?.ToString(CultureInfo.InvariantCulture),
            StartMonth = item.StartMonth.ToString(CultureInfo.InvariantCulture),
            EndMonth = item.EndMonth?.ToString(CultureInfo.InvariantCulture),
            Notes = item.Notes,
        };
    }

    /// <summary>
    /// Reads a planning month or a number of months: digits only. An empty value yields null.
    /// </summary>
    public static int? ParseMonth(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var isNumber = int.TryParse(
            value.Trim(),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var number
        );

        if (!isNumber)
        {
            throw new RuleException(InvalidCostError);
        }

        return number;
    }

    /// <summary>
    /// Reads an unsigned amount with an optional decimal point. An empty value yields null.
    /// </summary>
    private static decimal? ParseAmount(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var isNumber = decimal.TryParse(
            value.Trim(),
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var number
        );

        if (!isNumber)
        {
            throw new RuleException(InvalidCostError);
        }

        return number;
    }
}

/// <summary>The planning period as it is posted: a start month and a number of months.</summary>
public sealed class CostPlanningForm
{
    private const string InvalidCostPlanningError = "InvalidCostPlanning";

    /// <summary>The first month in the <c>yyyy-MM</c> form of a month input.</summary>
    public string Start { get; set; } = "";

    public string Months { get; set; } = "";

    public DateOnly ParseStart()
    {
        var isMonth = DateOnly.TryParseExact(
            Start + "-01",
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var start
        );

        if (!isMonth)
        {
            throw new RuleException(InvalidCostPlanningError);
        }

        return start;
    }

    public int ParseMonths()
    {
        return CostForm.ParseMonth(Months) ?? throw new RuleException(InvalidCostPlanningError);
    }

    public static CostPlanningForm From(CostPlanning? plan)
    {
        if (plan is null)
        {
            return new();
        }

        return new()
        {
            Start = plan.Start.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            Months = plan.Months.ToString(CultureInfo.InvariantCulture),
        };
    }
}
