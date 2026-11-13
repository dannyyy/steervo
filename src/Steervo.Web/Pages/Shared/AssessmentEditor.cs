using Steervo.Core;
namespace Steervo.Web.Pages.Shared;

public sealed record AssessmentEditor(Evaluation Evaluation, Criterion Criterion, Guid SubjectId, string SubjectName,
    string SubjectField, string Action, string FieldId, string PostUrl)
{
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
}
