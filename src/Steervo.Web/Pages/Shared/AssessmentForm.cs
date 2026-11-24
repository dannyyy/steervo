using System.Globalization;
using Steervo.Core;

namespace Steervo.Web.Pages.Shared;

/// <summary>Turns the posted fields of the assessment editor into an assessment.</summary>
public static class AssessmentForm
{
    private const string OptionalChoice = "optional";

    public static AssessmentInput ToInput(
        string? booleanValue,
        string? measurementValue,
        int? ratingValue,
        AssessmentSource source,
        string? notes
    )
    {
        var boolean = ParseBoolean(booleanValue);
        var measurement = ParseMeasurement(measurementValue);

        // "Optional" is stored as a flag next to an unknown yes/no value.
        var optionalAvailable = string.Equals(
            booleanValue?.Trim(),
            OptionalChoice,
            StringComparison.OrdinalIgnoreCase
        );

        return new(boolean, measurement, ratingValue, source, notes, optionalAvailable);
    }

    private static bool? ParseBoolean(string? booleanValue)
    {
        return booleanValue?.Trim().ToLowerInvariant() switch
        {
            null or "" or OptionalChoice => null,
            "true" => true,
            "false" => false,
            _ => throw new RuleException("InvalidValue"),
        };
    }

    private static decimal? ParseMeasurement(string? measurementValue)
    {
        if (string.IsNullOrWhiteSpace(measurementValue))
        {
            return null;
        }

        // The number input always submits a plain decimal with a point, whatever the page culture.
        var isNumber = decimal.TryParse(
            measurementValue,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var measurement
        );

        if (!isNumber)
        {
            throw new RuleException("InvalidValue");
        }

        return measurement;
    }
}
