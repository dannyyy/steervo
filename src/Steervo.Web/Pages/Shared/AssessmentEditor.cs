using System.Globalization;
using System.Text.Json;
using Steervo.Core;

namespace Steervo.Web.Pages.Shared;

/// <summary>
/// Everything the inline editor needs to assess one criterion for one subject (a car or an offer):
/// the current assessment, where the form posts to, and a rejected draft if there is one.
/// </summary>
public sealed record AssessmentEditor(
    Evaluation Evaluation,
    Criterion Criterion,
    Guid SubjectId,
    string SubjectName,
    string SubjectField,
    string Action,
    string FieldId,
    string PostUrl
)
{
    private const string OptionalChoice = "optional";

    public bool? BooleanValue { get; init; }

    public bool OptionalAvailable { get; init; }

    public decimal? MeasurementValue { get; init; }

    public int? RatingValue { get; init; }

    public AssessmentSource Source { get; init; }

    public string? Notes { get; init; }

    public bool HasAssessment { get; init; }

    public AssessmentDraft? Draft { get; init; }

    public string? ReviewNotice { get; init; }

    public string? SubmitLabel { get; init; }

    /// <summary>
    /// The option of the yes/no selection that reflects the current assessment: "true", "false",
    /// "optional", or null while nothing is known.
    /// </summary>
    public string? BooleanChoice
    {
        get
        {
            if (OptionalAvailable)
            {
                return OptionalChoice;
            }

            return BooleanValue?.ToString().ToLowerInvariant();
        }
    }

    /// <summary>The measured value in the culture-neutral notation a number input expects.</summary>
    public string? MeasurementText => MeasurementValue?.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The rejected draft as JSON, from which the form restores the person's input; null when
    /// there is no draft.
    /// </summary>
    public string? SubmittedValuesJson
    {
        get
        {
            if (Draft is null)
            {
                return null;
            }

            return JsonSerializer.Serialize(
                new
                {
                    booleanValue = Draft.BooleanValue,
                    measurementValue = Draft.MeasurementValue,
                    ratingValue = Draft.RatingValue,
                    source = Draft.Source,
                    notes = Draft.Notes,
                }
            );
        }
    }
}
