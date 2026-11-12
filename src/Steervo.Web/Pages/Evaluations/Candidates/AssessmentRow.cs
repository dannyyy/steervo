using Steervo.Core;
namespace Steervo.Web.Pages.Evaluations.Candidates;

public sealed record AssessmentRow(Evaluation Evaluation, Candidate Candidate, Criterion Criterion, AccessLevel Access, string PostUrl)
{
    public Assessment? Assessment => Evaluation.Assessments.SingleOrDefault(x => x.CandidateId == Candidate.Id && x.CriterionId == Criterion.Id);
    public Fulfillment Result => Evaluation.ResultFor(Candidate.Id, Criterion.Id);
    public string FieldId => $"assessment-{Candidate.Id}-{Criterion.Id}";
    public string ValueText => Criterion.Type switch
    {
        CriterionType.Measurement => Assessment?.MeasurementValue is { } value ? $"{value.ToString("G", System.Globalization.CultureInfo.CurrentCulture)} {Criterion.Unit}" : "",
        CriterionType.Rating => Assessment?.RatingValue is { } rating ? $"{rating} / 5" : "",
        _ => ""
    };
}
