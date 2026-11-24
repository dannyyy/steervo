using System.Globalization;
using Steervo.Core;
using Steervo.Web.Pages.Shared;

namespace Steervo.Web.Pages.Evaluations.Candidates;

/// <summary>One requirement of one car: the stored assessment, its result and what the views show for it.</summary>
public sealed record AssessmentRow(
    Evaluation Evaluation,
    Candidate Candidate,
    Criterion Criterion,
    AccessLevel Access,
    string PostUrl,
    AssessmentDraft? Draft = null
)
{
    private const string UnknownAnswerKey = "FulfillmentUnknown";

    public Assessment? Assessment =>
        Evaluation.Assessments.SingleOrDefault(assessment =>
            assessment.CandidateId == Candidate.Id && assessment.CriterionId == Criterion.Id
        );

    public Fulfillment Result => Evaluation.ResultFor(Candidate.Id, Criterion.Id);

    public string FieldId => $"assessment-{Candidate.Id}-{Criterion.Id}";

    /// <summary>True when a rejected submission for exactly this car and requirement is shown again.</summary>
    public bool HasDraft => Draft?.SubjectId == Candidate.Id && Draft.CriterionId == Criterion.Id;

    public bool CanEdit => Access >= AccessLevel.Editor;

    /// <summary>The measured or rated value as text; empty for yes/no requirements and missing values.</summary>
    public string ValueText
    {
        get
        {
            switch (Criterion.Type)
            {
                case CriterionType.Measurement:
                    if (Assessment?.MeasurementValue is { } measurement)
                    {
                        return $"{measurement.ToString("G", CultureInfo.CurrentCulture)} {Criterion.Unit}";
                    }

                    return "";

                case CriterionType.Rating:
                    if (Assessment?.RatingValue is { } rating)
                    {
                        return $"{rating} / 5";
                    }

                    return "";

                default:
                    return "";
            }
        }
    }

    /// <summary>
    /// Resource key of the answer when it is a fixed word (yes, no, optional, unknown);
    /// null when <see cref="ValueText"/> carries the answer.
    /// </summary>
    public string? AnswerKey
    {
        get
        {
            if (Criterion.Type == CriterionType.Boolean)
            {
                return BooleanAnswerKey(Assessment);
            }

            return ValueText.Length > 0 ? null : UnknownAnswerKey;
        }
    }

    /// <summary>The comparison sign shown in front of the threshold of a measurement requirement.</summary>
    public static string ThresholdSign(Criterion criterion) =>
        criterion.Comparison == Comparison.AtLeast ? "≥" : "≤";

    /// <summary>The model of the shared assessment editor, prefilled from the stored assessment.</summary>
    public AssessmentEditor ToEditor()
    {
        var assessment = Assessment;

        return new AssessmentEditor(
            Evaluation,
            Criterion,
            Candidate.Id,
            Candidate.DisplayName,
            "candidateId",
            "assess-candidate",
            FieldId,
            PostUrl
        )
        {
            BooleanValue = assessment?.BooleanValue,
            OptionalAvailable = assessment?.OptionalAvailable ?? false,
            MeasurementValue = assessment?.MeasurementValue,
            RatingValue = assessment?.RatingValue,
            Source = assessment?.Source ?? AssessmentSource.Unknown,
            Notes = assessment?.Notes,
            HasAssessment = assessment is not null,
            Draft = HasDraft ? Draft : null,
        };
    }

    // "Available as an option" outranks a stored yes/no; a missing answer stays distinct from "no".
    private static string BooleanAnswerKey(Assessment? assessment)
    {
        if (assessment?.OptionalAvailable == true)
        {
            return "BooleanOptional";
        }

        if (assessment?.BooleanValue is not { } answer)
        {
            return UnknownAnswerKey;
        }

        return answer ? "Yes" : "No";
    }
}
