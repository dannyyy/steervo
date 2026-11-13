using System.Globalization;
using Steervo.Core;

namespace Steervo.Web.Pages.Shared;

public static class AssessmentForm
{
    public static AssessmentInput ToInput(string? booleanValue, string? measurementValue, int? ratingValue,
        AssessmentSource source, string? notes)
    {
        bool? boolean = booleanValue?.Trim().ToLowerInvariant() switch
        {
            null or "" or "optional" => null,
            "true" => true,
            "false" => false,
            _ => throw new RuleException("InvalidValue")
        };
        decimal? measurement = null;
        if (!string.IsNullOrWhiteSpace(measurementValue))
        {
            if (!decimal.TryParse(measurementValue, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var parsed)) throw new RuleException("InvalidValue");
            measurement = parsed;
        }
        return new(boolean, measurement, ratingValue, source, notes,
            string.Equals(booleanValue?.Trim(), "optional", StringComparison.OrdinalIgnoreCase));
    }
}
