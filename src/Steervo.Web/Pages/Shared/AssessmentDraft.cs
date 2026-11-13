namespace Steervo.Web.Pages.Shared;

public sealed record AssessmentDraft(Guid SubjectId, Guid CriterionId, string? BooleanValue, string? MeasurementValue,
    string? RatingValue, string? Source, string? Notes, string Error);
