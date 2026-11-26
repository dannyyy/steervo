using System.Diagnostics.CodeAnalysis;
using Steervo.Core;

namespace Steervo.Application.Evaluations;

/// <summary>
/// Checks the shape of an imported file before anything is built from it: known format, size
/// limits, unique ids, and exactly the sections its format version defines.
/// </summary>
/// <remarks>
/// The checks run in a fixed order. Later checks rely on earlier ones, for example on entries
/// not being null, so the order must be kept.
/// </remarks>
internal static class EvaluationImportValidation
{
    private const int OldestVersion = 1;
    private const int CandidatesSinceVersion = 2;
    private const int OffersSinceVersion = 4;
    private const int CostsSinceVersion = 5;
    private const int ScenariosSinceVersion = 6;
    private const int OptionalAvailabilitySinceVersion = 7;
    private const int CountrySinceVersion = 8;

    private const int MaximumGroups = 100;
    private const int MaximumCriteria = 1000;
    private const int MaximumCandidates = 100;
    private const int MaximumAssessments = 100_000;
    private const int MaximumOffers = 1000;
    private const int MaximumRevisionsPerOffer = 1000;
    private const int MaximumOfferRevisions = 5000;
    private const int MaximumLinesPerRevision = 500;
    private const int MaximumCosts = 1000;
    private const int MaximumScenarios = 200;
    private const int MaximumResidualsPerScenario = 100;
    private const int MaximumAdjustmentsPerScenario = 1000;

    public static void Validate(EvaluationExport content)
    {
        RequireKnownFormat(content);
        RequireGroupsAndCriteria(content);
        RequireSectionsOfVersion(content);
        RequireValidScenarios(content.Scenarios ?? []);
        RequireValidCosts(content);
        RequireValidOffers(content);
        RequireValidCandidates(content);
        RequireFeaturesOfVersion(content);
    }

    private static void RequireKnownFormat(EvaluationExport content)
    {
        RejectIf(content.Format != EvaluationTransfer.FormatName);
        RejectIf(content.Version is < OldestVersion or > EvaluationTransfer.CurrentVersion);
    }

    private static void RequireGroupsAndCriteria(EvaluationExport content)
    {
        RejectIf(content.Groups is null || content.Criteria is null);
        RejectIf(content.Groups.Length > MaximumGroups);
        RejectIf(content.Criteria.Length > MaximumCriteria);

        RejectIf(
            content.Groups.Any(group =>
                group is null || group.Id == Guid.Empty || group.Position < 0
            )
        );
        RejectIf(
            content.Criteria.Any(criterion =>
                criterion is null || criterion.Id == Guid.Empty || criterion.Position < 0
            )
        );

        RejectIf(HasDuplicates(content.Groups, group => group.Id));
        RejectIf(HasDuplicates(content.Criteria, criterion => criterion.Id));
    }

    /// <summary>
    /// A section introduced by a later version must be absent in older files and present, even
    /// if empty, from that version on.
    /// </summary>
    private static void RequireSectionsOfVersion(EvaluationExport content)
    {
        if (content.Version < CandidatesSinceVersion)
        {
            RejectIf(content.Candidates is not null || content.Assessments is not null);
        }
        else
        {
            RejectIf(content.Candidates is null || content.Assessments is null);
        }

        if (content.Version < OffersSinceVersion)
        {
            RejectIf(
                content.Currency is not null
                    || content.Offers is not null
                    || content.OfferAssessments is not null
            );
        }
        else
        {
            // The currency stays optional: an evaluation without offers need not have one.
            RejectIf(content.Offers is null || content.OfferAssessments is null);
        }

        if (content.Version < CostsSinceVersion)
        {
            RejectIf(content.CostPlanning is not null || content.Costs is not null);
        }
        else
        {
            RejectIf(content.Costs is null);
        }

        if (content.Version < ScenariosSinceVersion)
        {
            RejectIf(content.CalculationSettings is not null || content.Scenarios is not null);
        }
        else
        {
            RejectIf(content.Scenarios is null);
        }
    }

    private static void RequireValidScenarios(ScenarioExport[] scenarios)
    {
        RejectIf(scenarios.Length > MaximumScenarios);
        RejectIf(scenarios.Any(IsMalformedScenario));
        RejectIf(HasDuplicates(scenarios, scenario => scenario.Id));
    }

    private static bool IsMalformedScenario(ScenarioExport scenario)
    {
        if (
            scenario is null
            || scenario.Id == Guid.Empty
            || scenario.Input is null
            || scenario.Input.Terms is null
        )
        {
            return true;
        }

        var residuals = scenario.Residuals;
        var adjustments = scenario.CostAdjustments;

        if (residuals is null || adjustments is null)
        {
            return true;
        }

        if (
            residuals.Length > MaximumResidualsPerScenario
            || adjustments.Length > MaximumAdjustmentsPerScenario
        )
        {
            return true;
        }

        if (
            residuals.Any(residual => residual is null)
            || adjustments.Any(adjustment => adjustment is null)
        )
        {
            return true;
        }

        return HasDuplicates(residuals, residual => residual.Month)
            || HasDuplicates(adjustments, adjustment => adjustment.CostId);
    }

    private static void RequireValidCosts(EvaluationExport content)
    {
        var costs = content.Costs ?? [];

        RejectIf(costs.Length > MaximumCosts);
        RejectIf(costs.Any(cost => cost is null || cost.Id == Guid.Empty || cost.Input is null));
        RejectIf(HasDuplicates(costs, cost => cost.Id));

        // Costs need a planning period and a currency to mean anything.
        var lacksCostBasis = content.CostPlanning is null || content.Currency is null;
        RejectIf(costs.Length > 0 && lacksCostBasis);
    }

    private static void RequireValidOffers(EvaluationExport content)
    {
        var offers = content.Offers ?? [];
        var offerAssessments = content.OfferAssessments ?? [];

        RejectIf(offers.Length > MaximumOffers);
        RejectIf(offerAssessments.Length > MaximumAssessments);

        RejectIf(offers.Any(HasNoUsableRevisions));
        RejectIf(HasDuplicates(offers, offer => offer.Id));
        RejectIf(offers.Sum(offer => offer.Revisions.Length) > MaximumOfferRevisions);
        RejectIf(offers.SelectMany(offer => offer.Revisions).Any(IsMalformedRevision));

        RejectIf(offerAssessments.Any(assessment => assessment is null));
        RejectIf(
            HasDuplicates(offerAssessments, assessment =>
                (assessment.OfferId, assessment.CriterionId)
            )
        );

        RejectIf(offers.Length > 0 && content.Currency is null);
    }

    private static bool HasNoUsableRevisions(OfferExport offer)
    {
        return offer is null
            || offer.Id == Guid.Empty
            || offer.Revisions is null
            || offer.Revisions.Length is < 1 or > MaximumRevisionsPerOffer;
    }

    private static bool IsMalformedRevision(OfferRevisionExport revision)
    {
        return revision is null
            || revision.Terms is null
            || revision.Lines is null
            || revision.Lines.Length > MaximumLinesPerRevision
            || revision.Lines.Any(line => line is null);
    }

    private static void RequireValidCandidates(EvaluationExport content)
    {
        var candidates = content.Candidates ?? [];
        var assessments = content.Assessments ?? [];

        RejectIf(candidates.Length > MaximumCandidates);
        RejectIf(assessments.Length > MaximumAssessments);

        RejectIf(
            candidates.Any(candidate =>
                candidate is null || candidate.Id == Guid.Empty || candidate.Position < 0
            )
        );
        RejectIf(HasDuplicates(candidates, candidate => candidate.Id));

        RejectIf(assessments.Any(assessment => assessment is null));
        RejectIf(
            HasDuplicates(assessments, assessment =>
                (assessment.CandidateId, assessment.CriterionId)
            )
        );
    }

    /// <summary>Values a format version did not know yet must not appear in its files.</summary>
    private static void RequireFeaturesOfVersion(EvaluationExport content)
    {
        var assessments = content.Assessments ?? [];
        var offerAssessments = content.OfferAssessments ?? [];

        if (content.Version < OptionalAvailabilitySinceVersion)
        {
            RejectIf(
                assessments.Any(assessment => assessment.OptionalAvailable)
                    || offerAssessments.Any(assessment => assessment.OptionalAvailable)
            );
        }

        if (content.Version < CountrySinceVersion)
        {
            RejectIf(content.Country is not null);
        }
    }

    private static bool HasDuplicates<TItem, TKey>(TItem[] items, Func<TItem, TKey> key)
    {
        return items.DistinctBy(key).Count() != items.Length;
    }

    private static void RejectIf([DoesNotReturnIf(true)] bool invalid)
    {
        if (invalid)
        {
            throw new RuleException(EvaluationTransfer.InvalidImport);
        }
    }
}
