using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Steervo.Core;

namespace Steervo.Application.Evaluations;

public sealed record GroupExport(Guid Id, string Name, int Position);

public sealed record CriterionExport(
    Guid Id,
    string Name,
    CriterionType Type,
    Priority Priority,
    CriterionScope Scope,
    Guid? GroupId,
    decimal? Threshold,
    string? Unit,
    Comparison Comparison,
    int Position
);

public sealed record CandidateExport(
    Guid Id,
    string Model,
    string Brand,
    string Variant,
    CandidateCondition Condition,
    int? Year,
    int? Mileage,
    string Notes,
    string Links,
    CandidateStatus Status,
    string StatusReason,
    int Position,
    VehicleSpecifications? Specifications = null
);

public sealed record AssessmentExport(
    Guid CandidateId,
    Guid CriterionId,
    bool? BooleanValue,
    decimal? MeasurementValue,
    int? RatingValue,
    AssessmentSource Source,
    string Notes,
    bool OptionalAvailable = false
);

public sealed record OfferRevisionExport(
    int Number,
    DateTimeOffset CreatedAt,
    string ChangeNote,
    OfferTerms Terms,
    OfferLineInput[] Lines
);

public sealed record OfferExport(Guid Id, Guid CandidateId, OfferRevisionExport[] Revisions);

public sealed record OfferAssessmentExport(
    Guid OfferId,
    Guid CriterionId,
    int RevisionNumber,
    bool? BooleanValue,
    decimal? MeasurementValue,
    int? RatingValue,
    AssessmentSource Source,
    string Notes,
    bool OptionalAvailable = false
);

public sealed record CostPlanningExport(DateOnly Start, int Months);

public sealed record CostExport(Guid Id, CostInput Input);

public sealed record ScenarioExport(
    Guid Id,
    ScenarioInput Input,
    ResidualInput[] Residuals,
    CostAdjustmentInput[] CostAdjustments
);

public sealed record CalculationSettingsExport(decimal AnnualReturnPercent);

public sealed record EvaluationExport(
    string Format,
    int Version,
    string Name,
    string Purpose,
    GroupExport[] Groups,
    CriterionExport[] Criteria,
    CandidateExport[]? Candidates = null,
    AssessmentExport[]? Assessments = null,
    string? Currency = null,
    OfferExport[]? Offers = null,
    OfferAssessmentExport[]? OfferAssessments = null,
    CostPlanningExport? CostPlanning = null,
    CostExport[]? Costs = null,
    CalculationSettingsExport? CalculationSettings = null,
    ScenarioExport[]? Scenarios = null,
    string? Country = null
);

/// <summary>
/// Exports an evaluation as a JSON file and imports such files, including those written by
/// earlier format versions.
/// </summary>
public sealed class EvaluationTransfer(
    EvaluationWorkspace workspace,
    IEvaluationStore store,
    ICurrentUser current
)
{
    public const int MaximumBytes = 5_000_000;

    public const string LegacyVehicleLine = "Fahrzeug";

    internal const string FormatName = "steervo-evaluation";
    internal const int CurrentVersion = 9;
    internal const string InvalidImport = "InvalidImport";

    private const int MaximumDepth = 16;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        MaxDepth = MaximumDepth,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    public async Task<byte[]> ExportAsync(Guid id)
    {
        var evaluation = await workspace.ReadAsync(id);

        // Documents do not fit into a plain JSON file; they are exported as an archive.
        if (evaluation.Documents.Count > 0)
        {
            throw new RuleException("DocumentsRequireArchive");
        }

        return Serialize(evaluation);
    }

    public async Task<Guid> ImportAsync(byte[] data)
    {
        var prepared = PrepareImport(data);

        await store.CreateAsync(prepared.Evaluation);

        return prepared.Evaluation.Id;
    }

    internal static byte[] Serialize(Evaluation evaluation)
    {
        var content = EvaluationExportMapping.ToExport(evaluation);

        return JsonSerializer.SerializeToUtf8Bytes(content, Json);
    }

    /// <summary>
    /// Builds a new evaluation owned by the current user from an exported file without storing
    /// it. Every entity receives a new id; the returned map leads from exported to new offer ids.
    /// </summary>
    internal (Evaluation Evaluation, Dictionary<Guid, Guid> OfferIds) PrepareImport(byte[] data)
    {
        var content = ReadContent(data);
        EvaluationImportValidation.Validate(content);

        var evaluation = new Evaluation(current.Id, content.Name, content.Purpose);

        if (content.Country is not null)
        {
            evaluation.SetCountry(content.Country);
        }

        // Each step maps exported ids to the new ones; later steps resolve their references
        // through these maps, so the order follows the dependencies.
        var groupIds = ImportGroups(evaluation, content.Groups);
        var criterionIds = ImportCriteria(evaluation, content.Criteria, groupIds);
        var candidateIds = ImportCandidates(evaluation, content.Candidates ?? []);
        ImportAssessments(evaluation, content.Assessments ?? [], candidateIds, criterionIds);

        if (content.Currency is not null)
        {
            evaluation.SetCurrency(content.Currency);
        }

        var offerIds = ImportOffers(evaluation, content.Offers ?? [], candidateIds, criterionIds);
        ImportOfferAssessments(evaluation, content.OfferAssessments ?? [], offerIds, criterionIds);

        if (content.CostPlanning is { } plan)
        {
            evaluation.SetCostPlanning(plan.Start, plan.Months);
        }

        var costIds = ImportCosts(evaluation, content.Costs ?? [], candidateIds);

        if (content.CalculationSettings is { } calculation)
        {
            evaluation.SetCalculationSettings(calculation.AnnualReturnPercent);
        }

        ImportScenarios(evaluation, content.Scenarios ?? [], offerIds, costIds);

        return (evaluation, offerIds);
    }

    private static EvaluationExport ReadContent(byte[] data)
    {
        if (data.Length > MaximumBytes)
        {
            throw new RuleException(InvalidImport);
        }

        try
        {
            var root = JsonNode.Parse(data, documentOptions: new() { MaxDepth = MaximumDepth });
            var upgraded = LegacyExportUpgrade.Apply(root);

            return upgraded.Deserialize<EvaluationExport>(Json)
                ?? throw new RuleException(InvalidImport);
        }
        catch (Exception error)
            when (error is JsonException or InvalidOperationException or FormatException)
        {
            throw new RuleException(InvalidImport);
        }
    }

    private static Dictionary<Guid, Guid> ImportGroups(Evaluation evaluation, GroupExport[] groups)
    {
        var groupIds = new Dictionary<Guid, Guid>();

        foreach (var group in groups.OrderBy(group => group.Position))
        {
            groupIds.Add(group.Id, evaluation.AddGroup(group.Name));
        }

        return groupIds;
    }

    private static Dictionary<Guid, Guid> ImportCriteria(
        Evaluation evaluation,
        CriterionExport[] criteria,
        Dictionary<Guid, Guid> groupIds
    )
    {
        var criterionIds = new Dictionary<Guid, Guid>();

        foreach (var criterion in criteria.OrderBy(criterion => criterion.Position))
        {
            Guid? groupId = null;

            if (criterion.GroupId is { } exportedGroupId)
            {
                groupId = Resolve(groupIds, exportedGroupId);
            }

            var input = new CriterionInput(
                criterion.Name,
                criterion.Type,
                criterion.Priority,
                criterion.Scope,
                groupId,
                criterion.Threshold,
                criterion.Unit,
                criterion.Comparison
            );

            criterionIds.Add(criterion.Id, evaluation.AddCriterion(input));
        }

        return criterionIds;
    }

    private static Dictionary<Guid, Guid> ImportCandidates(
        Evaluation evaluation,
        CandidateExport[] candidates
    )
    {
        var candidateIds = new Dictionary<Guid, Guid>();

        foreach (var candidate in candidates.OrderBy(candidate => candidate.Position))
        {
            var input = new CandidateInput(
                candidate.Model,
                candidate.Brand,
                candidate.Variant,
                candidate.Condition,
                candidate.Year,
                candidate.Mileage,
                candidate.Notes,
                candidate.Links,
                candidate.Specifications
            );

            var candidateId = evaluation.AddCandidate(input);
            evaluation.ClassifyCandidate(candidateId, candidate.Status, candidate.StatusReason);
            candidateIds.Add(candidate.Id, candidateId);
        }

        return candidateIds;
    }

    private static void ImportAssessments(
        Evaluation evaluation,
        AssessmentExport[] assessments,
        Dictionary<Guid, Guid> candidateIds,
        Dictionary<Guid, Guid> criterionIds
    )
    {
        foreach (var assessment in assessments)
        {
            var candidateId = Resolve(candidateIds, assessment.CandidateId);
            var criterionId = Resolve(criterionIds, assessment.CriterionId);

            var input = new AssessmentInput(
                assessment.BooleanValue,
                assessment.MeasurementValue,
                assessment.RatingValue,
                assessment.Source,
                assessment.Notes,
                assessment.OptionalAvailable
            );

            evaluation.AssessCandidate(candidateId, criterionId, input);
        }
    }

    private static Dictionary<Guid, Guid> ImportOffers(
        Evaluation evaluation,
        OfferExport[] offers,
        Dictionary<Guid, Guid> candidateIds,
        Dictionary<Guid, Guid> criterionIds
    )
    {
        var offerIds = new Dictionary<Guid, Guid>();

        foreach (var offer in offers)
        {
            var candidateId = Resolve(candidateIds, offer.CandidateId);
            var history = offer.Revisions.OrderBy(revision => revision.Number).ToArray();

            // The versions of an offer are numbered 1, 2, 3, ... without gaps.
            var isNumberedWithoutGaps = history
                .Select(revision => revision.Number)
                .SequenceEqual(Enumerable.Range(1, history.Length));

            if (!isNumberedWithoutGaps)
            {
                throw new RuleException(InvalidImport);
            }

            var first = history[0];
            var offerId = evaluation.AddOffer(
                candidateId,
                first.Terms,
                LinesWithNewCriterionIds(first, criterionIds),
                changeNote: first.ChangeNote,
                createdAt: first.CreatedAt
            );

            foreach (var revision in history.Skip(1))
            {
                evaluation.ReviseOffer(
                    offerId,
                    revision.Terms,
                    LinesWithNewCriterionIds(revision, criterionIds),
                    revision.ChangeNote,
                    revision.CreatedAt
                );
            }

            offerIds.Add(offer.Id, offerId);
        }

        return offerIds;
    }

    private static OfferLineInput[] LinesWithNewCriterionIds(
        OfferRevisionExport revision,
        Dictionary<Guid, Guid> criterionIds
    )
    {
        var lines = new List<OfferLineInput>();

        foreach (var line in revision.Lines)
        {
            if (line.CriterionId is { } exportedCriterionId)
            {
                lines.Add(line with { CriterionId = Resolve(criterionIds, exportedCriterionId) });
            }
            else
            {
                lines.Add(line);
            }
        }

        return lines.ToArray();
    }

    private static void ImportOfferAssessments(
        Evaluation evaluation,
        OfferAssessmentExport[] offerAssessments,
        Dictionary<Guid, Guid> offerIds,
        Dictionary<Guid, Guid> criterionIds
    )
    {
        foreach (var exported in offerAssessments)
        {
            var offerId = Resolve(offerIds, exported.OfferId);
            var criterionId = Resolve(criterionIds, exported.CriterionId);

            var input = new AssessmentInput(
                exported.BooleanValue,
                exported.MeasurementValue,
                exported.RatingValue,
                exported.Source,
                exported.Notes,
                exported.OptionalAvailable
            );

            evaluation.AssessOffer(offerId, criterionId, input);

            // Assessing binds the value to the latest offer version; the file states the version
            // it was actually made for.
            var imported = evaluation.OfferAssessments.Single(assessment =>
                assessment.OfferId == offerId && assessment.CriterionId == criterionId
            );
            imported.RestoreRevision(evaluation.FindOffer(offerId), exported.RevisionNumber);
        }
    }

    private static Dictionary<Guid, Guid> ImportCosts(
        Evaluation evaluation,
        CostExport[] costs,
        Dictionary<Guid, Guid> candidateIds
    )
    {
        var costIds = new Dictionary<Guid, Guid>();

        foreach (var cost in costs)
        {
            Guid? candidateId = null;

            if (cost.Input.CandidateId is { } exportedCandidateId)
            {
                candidateId = Resolve(candidateIds, exportedCandidateId);
            }

            var input = cost.Input with { CandidateId = candidateId };

            costIds.Add(cost.Id, evaluation.AddCost(input));
        }

        return costIds;
    }

    private static void ImportScenarios(
        Evaluation evaluation,
        ScenarioExport[] scenarios,
        Dictionary<Guid, Guid> offerIds,
        Dictionary<Guid, Guid> costIds
    )
    {
        foreach (var scenario in scenarios)
        {
            var offerId = Resolve(offerIds, scenario.Input.OfferId);
            var scenarioId = evaluation.AddScenario(scenario.Input with { OfferId = offerId });

            foreach (var residual in scenario.Residuals)
            {
                evaluation.SetResidual(scenarioId, residual);
            }

            foreach (var adjustment in scenario.CostAdjustments)
            {
                var costId = Resolve(costIds, adjustment.CostId);

                evaluation.SetCostAdjustment(scenarioId, adjustment with { CostId = costId });
            }
        }
    }

    /// <summary>Translates an exported id into the id of the imported entity.</summary>
    private static Guid Resolve(Dictionary<Guid, Guid> newIds, Guid exportedId)
    {
        if (!newIds.TryGetValue(exportedId, out var newId))
        {
            throw new RuleException(InvalidImport);
        }

        return newId;
    }
}
