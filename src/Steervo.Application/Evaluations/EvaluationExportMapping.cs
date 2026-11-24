using Steervo.Core;

namespace Steervo.Application.Evaluations;

/// <summary>Maps an evaluation to the export model of the current format version.</summary>
internal static class EvaluationExportMapping
{
    public static EvaluationExport ToExport(Evaluation evaluation)
    {
        return new EvaluationExport(
            Format: EvaluationTransfer.FormatName,
            Version: EvaluationTransfer.CurrentVersion,
            Name: evaluation.Name,
            Purpose: evaluation.Purpose,
            Groups: Groups(evaluation),
            Criteria: Criteria(evaluation),
            Candidates: Candidates(evaluation),
            Assessments: Assessments(evaluation),
            Currency: evaluation.Currency,
            Offers: Offers(evaluation),
            OfferAssessments: OfferAssessments(evaluation),
            CostPlanning: CostPlanning(evaluation),
            Costs: Costs(evaluation),
            CalculationSettings: CalculationSettings(evaluation),
            Scenarios: Scenarios(evaluation),
            Country: evaluation.Country
        );
    }

    private static GroupExport[] Groups(Evaluation evaluation)
    {
        return evaluation
            .Groups.OrderBy(group => group.Position)
            .Select(group => new GroupExport(group.Id, group.Name, group.Position))
            .ToArray();
    }

    private static CriterionExport[] Criteria(Evaluation evaluation)
    {
        return evaluation
            .Criteria.OrderBy(criterion => criterion.Position)
            .Select(criterion => new CriterionExport(
                criterion.Id,
                criterion.Name,
                criterion.Type,
                criterion.Priority,
                criterion.Scope,
                criterion.GroupId,
                criterion.Threshold,
                criterion.Unit,
                criterion.Comparison,
                criterion.Position
            ))
            .ToArray();
    }

    private static CandidateExport[] Candidates(Evaluation evaluation)
    {
        return evaluation
            .Candidates.OrderBy(candidate => candidate.Position)
            .Select(candidate => new CandidateExport(
                candidate.Id,
                candidate.Model,
                candidate.Brand,
                candidate.Variant,
                candidate.Condition,
                candidate.Year,
                candidate.Mileage,
                candidate.Notes,
                candidate.Links,
                candidate.Status,
                candidate.StatusReason,
                candidate.Position,
                candidate.Specifications
            ))
            .ToArray();
    }

    private static AssessmentExport[] Assessments(Evaluation evaluation)
    {
        return evaluation
            .Assessments.OrderBy(assessment => assessment.CandidateId)
            .ThenBy(assessment => assessment.CriterionId)
            .Select(assessment => new AssessmentExport(
                assessment.CandidateId,
                assessment.CriterionId,
                assessment.BooleanValue,
                assessment.MeasurementValue,
                assessment.RatingValue,
                assessment.Source,
                assessment.Notes,
                assessment.OptionalAvailable
            ))
            .ToArray();
    }

    private static OfferExport[] Offers(Evaluation evaluation)
    {
        var knownCriteria = evaluation.Criteria.Select(criterion => criterion.Id).ToHashSet();

        return evaluation
            .Offers.OrderBy(offer => offer.Position)
            .Select(offer => new OfferExport(
                offer.Id,
                offer.CandidateId,
                offer
                    .Revisions.OrderBy(revision => revision.Number)
                    .Select(revision => Revision(revision, knownCriteria))
                    .ToArray()
            ))
            .ToArray();
    }

    private static OfferRevisionExport Revision(OfferRevision revision, HashSet<Guid> knownCriteria)
    {
        var lines = revision
            .Lines.OrderBy(line => line.Position)
            .Select(line => line.ToInput())
            .Select(line => WithoutDeletedCriterion(line, knownCriteria))
            .ToArray();

        return new OfferRevisionExport(
            revision.Number,
            revision.CreatedAt,
            revision.ChangeNote,
            revision.Terms,
            lines
        );
    }

    // Stored versions never change, so a line may still name a requirement that was deleted later.
    private static OfferLineInput WithoutDeletedCriterion(
        OfferLineInput line,
        HashSet<Guid> knownCriteria
    )
    {
        var namesDeletedCriterion =
            line.CriterionId is { } linked && !knownCriteria.Contains(linked);

        return namesDeletedCriterion ? line with { CriterionId = null } : line;
    }

    private static OfferAssessmentExport[] OfferAssessments(Evaluation evaluation)
    {
        return evaluation
            .OfferAssessments.Select(assessment => new OfferAssessmentExport(
                assessment.OfferId,
                assessment.CriterionId,
                assessment.RevisionNumber,
                assessment.BooleanValue,
                assessment.MeasurementValue,
                assessment.RatingValue,
                assessment.Source,
                assessment.Notes,
                assessment.OptionalAvailable
            ))
            .ToArray();
    }

    private static CostPlanningExport? CostPlanning(Evaluation evaluation)
    {
        if (evaluation.CostPlanning is not { } plan)
        {
            return null;
        }

        return new CostPlanningExport(plan.Start, plan.Months);
    }

    private static CostExport[] Costs(Evaluation evaluation)
    {
        return evaluation.Costs.Select(cost => new CostExport(cost.Id, cost.ToInput())).ToArray();
    }

    private static CalculationSettingsExport? CalculationSettings(Evaluation evaluation)
    {
        if (evaluation.CalculationSettings is not { } settings)
        {
            return null;
        }

        return new CalculationSettingsExport(settings.AnnualReturnPercent);
    }

    private static ScenarioExport[] Scenarios(Evaluation evaluation)
    {
        return evaluation
            .Scenarios.Select(scenario => new ScenarioExport(
                scenario.Id,
                scenario.ToInput(),
                scenario.Residuals.Select(residual => residual.ToInput()).ToArray(),
                scenario.CostAdjustments.Select(adjustment => adjustment.ToInput()).ToArray()
            ))
            .ToArray();
    }
}
