namespace Steervo.Web.Pages.Shared;

/// <summary>
/// A rejected assessment exactly as it was submitted, together with the reason, so that the
/// editor can open again with the person's input.
/// </summary>
public sealed record AssessmentDraft(
    Guid SubjectId,
    Guid CriterionId,
    string? BooleanValue,
    string? MeasurementValue,
    string? RatingValue,
    string? Source,
    string? Notes,
    string Error
);
