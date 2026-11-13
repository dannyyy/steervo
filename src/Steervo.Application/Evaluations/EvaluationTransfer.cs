using System.Text.Json;
using System.Text.Json.Serialization;
using Steervo.Core;

namespace Steervo.Application.Evaluations;

public sealed record GroupExport(Guid Id, string Name, int Position);
public sealed record CriterionExport(Guid Id, string Name, CriterionType Type, Priority Priority, CriterionScope Scope, Guid? GroupId, decimal? Threshold, string? Unit, Comparison Comparison, int Position);
public sealed record CandidateExport(Guid Id, string Model, string Brand, string Variant, CandidateCondition Condition, int? Year, int? Mileage,
    string Notes, string Links, CandidateStatus Status, string StatusReason, int Position, VehicleSpecifications? Specifications = null);
public sealed record AssessmentExport(Guid CandidateId, Guid CriterionId, bool? BooleanValue, decimal? MeasurementValue, int? RatingValue, AssessmentSource Source, string Notes, bool OptionalAvailable = false);
public sealed record OfferRevisionExport(int Number, DateTimeOffset CreatedAt, string ChangeNote, OfferTerms Terms, OfferLineInput[] Lines);
public sealed record OfferExport(Guid Id, Guid CandidateId, bool Archived, OfferRevisionExport[] Revisions);
public sealed record OfferAssessmentExport(Guid OfferId, Guid CriterionId, int RevisionNumber, bool? BooleanValue, decimal? MeasurementValue, int? RatingValue, AssessmentSource Source, string Notes, bool OptionalAvailable = false);
public sealed record CostPlanningExport(DateOnly Start, int Months);
public sealed record CostExport(Guid Id, CostInput Input);
public sealed record ScenarioExport(Guid Id, ScenarioInput Input, ResidualInput[] Residuals, CostAdjustmentInput[] CostAdjustments);
public sealed record CalculationSettingsExport(decimal AnnualReturnPercent);
public sealed record EvaluationExport(string Format, int Version, string Name, string Purpose, GroupExport[] Groups, CriterionExport[] Criteria,
    CandidateExport[]? Candidates = null, AssessmentExport[]? Assessments = null, string? Currency = null, OfferExport[]? Offers = null, OfferAssessmentExport[]? OfferAssessments = null,
    CostPlanningExport? CostPlanning = null, CostExport[]? Costs = null, CalculationSettingsExport? CalculationSettings = null, ScenarioExport[]? Scenarios = null);

public sealed class EvaluationTransfer(EvaluationWorkspace workspace, IEvaluationStore store, ICurrentUser current)
{
    public const int MaximumBytes = 5_000_000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true, MaxDepth = 16, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    public async Task<byte[]> ExportAsync(Guid id)
    {
        var evaluation = await workspace.ReadAsync(id);
        if (evaluation.Documents.Count > 0) throw new RuleException("DocumentsRequireArchive");
        return Serialize(evaluation);
    }
    internal static byte[] Serialize(Evaluation evaluation)
    {
        var content = new EvaluationExport("steervo-evaluation", 7, evaluation.Name, evaluation.Purpose,
            evaluation.Groups.OrderBy(x => x.Position).Select(x => new GroupExport(x.Id, x.Name, x.Position)).ToArray(),
            evaluation.Criteria.OrderBy(x => x.Position).Select(x => new CriterionExport(x.Id, x.Name, x.Type, x.Priority, x.Scope, x.GroupId, x.Threshold, x.Unit, x.Comparison, x.Position)).ToArray(),
            evaluation.Candidates.OrderBy(x => x.Position).Select(x => new CandidateExport(x.Id, x.Model, x.Brand, x.Variant, x.Condition, x.Year, x.Mileage, x.Notes, x.Links, x.Status, x.StatusReason, x.Position, x.Specifications)).ToArray(),
            evaluation.Assessments.OrderBy(x => x.CandidateId).ThenBy(x => x.CriterionId).Select(x => new AssessmentExport(x.CandidateId, x.CriterionId, x.BooleanValue, x.MeasurementValue, x.RatingValue, x.Source, x.Notes, x.OptionalAvailable)).ToArray(), evaluation.Currency,
            evaluation.Offers.Select(offer => new OfferExport(offer.Id, offer.CandidateId, offer.Archived,
                offer.Revisions.OrderBy(revision => revision.Number).Select(revision => new OfferRevisionExport(revision.Number, revision.CreatedAt, revision.ChangeNote, revision.Terms,
                    revision.Lines.OrderBy(line => line.Position).Select(line => line.ToInput()).ToArray())).ToArray())).ToArray(),
            evaluation.OfferAssessments.Select(value => new OfferAssessmentExport(value.OfferId, value.CriterionId, value.RevisionNumber, value.BooleanValue, value.MeasurementValue, value.RatingValue, value.Source, value.Notes, value.OptionalAvailable)).ToArray(),
            evaluation.CostPlanning is { } plan ? new(plan.Start, plan.Months) : null,
            evaluation.Costs.Select(cost => new CostExport(cost.Id, cost.ToInput())).ToArray(),
            evaluation.CalculationSettings is { } settings ? new(settings.AnnualReturnPercent) : null,
            evaluation.Scenarios.Select(s => new ScenarioExport(s.Id, s.ToInput(), s.Residuals.Select(r => r.ToInput()).ToArray(), s.CostAdjustments.Select(a => a.ToInput()).ToArray())).ToArray());
        return JsonSerializer.SerializeToUtf8Bytes(content, Json);
    }
    public async Task<Guid> ImportAsync(byte[] data)
    {
        var prepared = PrepareImport(data);
        await store.CreateAsync(prepared.Evaluation);
        return prepared.Evaluation.Id;
    }
    internal (Evaluation Evaluation, Dictionary<Guid, Guid> OfferIds) PrepareImport(byte[] data)
    {
        if (data.Length > MaximumBytes) throw new RuleException("InvalidImport");
        EvaluationExport content;
        try { content = JsonSerializer.Deserialize<EvaluationExport>(data, Json) ?? throw new RuleException("InvalidImport"); }
        catch (JsonException) { throw new RuleException("InvalidImport"); }
        if (content.Format != "steervo-evaluation" || content.Version is not (1 or 2 or 3 or 4 or 5 or 6 or 7) || content.Groups is null || content.Criteria is null || content.Groups.Length > 100 || content.Criteria.Length > 1000
            || content.Groups.Any(x => x is null || x.Id == Guid.Empty || x.Position < 0) || content.Criteria.Any(x => x is null || x.Id == Guid.Empty || x.Position < 0)
            || content.Groups.DistinctBy(x => x.Id).Count() != content.Groups.Length || content.Criteria.DistinctBy(x => x.Id).Count() != content.Criteria.Length
            || (content.Version == 1 && (content.Candidates is not null || content.Assessments is not null))
            || (content.Version >= 2 && (content.Candidates is null || content.Assessments is null))) throw new RuleException("InvalidImport");
        if ((content.Version < 4 && (content.Currency is not null || content.Offers is not null || content.OfferAssessments is not null))
            || (content.Version >= 4 && (content.Offers is null || content.OfferAssessments is null))) throw new RuleException("InvalidImport");
        if ((content.Version < 5 && (content.CostPlanning is not null || content.Costs is not null)) || (content.Version >= 5 && content.Costs is null)) throw new RuleException("InvalidImport");
        if ((content.Version < 6 && (content.CalculationSettings is not null || content.Scenarios is not null)) || (content.Version >= 6 && content.Scenarios is null)) throw new RuleException("InvalidImport");
        var scenarios = content.Scenarios ?? [];
        if (scenarios.Length > 200 || scenarios.Any(s => s is null || s.Id == Guid.Empty || s.Input is null || s.Input.Terms is null
                || s.Residuals is null || s.CostAdjustments is null || s.Residuals.Length > 100 || s.CostAdjustments.Length > 1000
                || s.Residuals.Any(r => r is null) || s.CostAdjustments.Any(a => a is null)
                || s.Residuals.DistinctBy(r => r.Month).Count() != s.Residuals.Length || s.CostAdjustments.DistinctBy(a => a.CostId).Count() != s.CostAdjustments.Length)
            || scenarios.DistinctBy(s => s.Id).Count() != scenarios.Length) throw new RuleException("InvalidImport");
        var costs = content.Costs ?? [];
        if (costs.Length > 1000 || costs.Any(c => c is null || c.Id == Guid.Empty || c.Input is null)
            || costs.DistinctBy(c => c.Id).Count() != costs.Length || (costs.Length > 0 && (content.CostPlanning is null || content.Currency is null))) throw new RuleException("InvalidImport");
        var offers = content.Offers ?? [];
        var offerAssessments = content.OfferAssessments ?? [];
        if (offers.Length > 1000 || offerAssessments.Length > 100_000 || offers.Any(offer => offer is null || offer.Id == Guid.Empty || offer.Revisions is null || offer.Revisions.Length is < 1 or > 1000)
            || offers.DistinctBy(offer => offer.Id).Count() != offers.Length || offers.Sum(offer => offer.Revisions.Length) > 5000
            || offers.SelectMany(offer => offer.Revisions).Any(revision => revision is null || revision.Terms is null || revision.Lines is null || revision.Lines.Length > 500 || revision.Lines.Any(line => line is null))
            || offerAssessments.Any(value => value is null) || offerAssessments.DistinctBy(value => (value.OfferId, value.CriterionId)).Count() != offerAssessments.Length
            || (offers.Length > 0 && content.Currency is null)) throw new RuleException("InvalidImport");
        var candidates = content.Candidates ?? [];
        var assessments = content.Assessments ?? [];
        if (candidates.Length > 100 || assessments.Length > 100_000
            || candidates.Any(x => x is null || x.Id == Guid.Empty || x.Position < 0)
            || candidates.DistinctBy(x => x.Id).Count() != candidates.Length
            || assessments.Any(x => x is null)
            || assessments.DistinctBy(x => (x.CandidateId, x.CriterionId)).Count() != assessments.Length) throw new RuleException("InvalidImport");
        if (content.Version < 7 && (assessments.Any(x => x.OptionalAvailable) || offerAssessments.Any(x => x.OptionalAvailable))) throw new RuleException("InvalidImport");
        var evaluation = new Evaluation(current.Id, content.Name, content.Purpose);
        var groups = content.Groups.OrderBy(x => x.Position).ToDictionary(x => x.Id, x => evaluation.AddGroup(x.Name));
        var criteria = new Dictionary<Guid, Guid>();
        foreach (var criterion in content.Criteria.OrderBy(x => x.Position))
        {
            Guid? groupId = null;
            if (criterion.GroupId is { } original)
            {
                if (!groups.TryGetValue(original, out var mapped)) throw new RuleException("InvalidImport");
                groupId = mapped;
            }
            criteria.Add(criterion.Id, evaluation.AddCriterion(new(criterion.Name, criterion.Type, criterion.Priority, criterion.Scope, groupId, criterion.Threshold, criterion.Unit, criterion.Comparison)));
        }
        var candidateIds = new Dictionary<Guid, Guid>();
        foreach (var candidate in candidates.OrderBy(x => x.Position))
        {
            var id = evaluation.AddCandidate(new(candidate.Model, candidate.Brand, candidate.Variant, candidate.Condition, candidate.Year, candidate.Mileage, candidate.Notes, candidate.Links, candidate.Specifications));
            evaluation.ClassifyCandidate(id, candidate.Status, candidate.StatusReason);
            candidateIds.Add(candidate.Id, id);
        }
        foreach (var assessment in assessments)
        {
            if (!candidateIds.TryGetValue(assessment.CandidateId, out var candidateId) || !criteria.TryGetValue(assessment.CriterionId, out var criterionId)) throw new RuleException("InvalidImport");
            evaluation.AssessCandidate(candidateId, criterionId, new(assessment.BooleanValue, assessment.MeasurementValue, assessment.RatingValue, assessment.Source, assessment.Notes, assessment.OptionalAvailable));
        }
        if (content.Currency is not null) evaluation.SetCurrency(content.Currency);
        var offerIds = new Dictionary<Guid, Guid>();
        foreach (var offer in offers)
        {
            if (!candidateIds.TryGetValue(offer.CandidateId, out var candidateId)) throw new RuleException("InvalidImport");
            var history = offer.Revisions.OrderBy(revision => revision.Number).ToArray();
            if (!history.Select(revision => revision.Number).SequenceEqual(Enumerable.Range(1, history.Length))) throw new RuleException("InvalidImport");
            var first = history[0];
            var id = evaluation.AddOffer(candidateId, first.Terms, first.Lines, changeNote: first.ChangeNote, createdAt: first.CreatedAt);
            foreach (var revision in history.Skip(1)) evaluation.ReviseOffer(id, revision.Terms, revision.Lines, revision.ChangeNote, revision.CreatedAt);
            offerIds.Add(offer.Id, id);
        }
        foreach (var value in offerAssessments)
        {
            if (!offerIds.TryGetValue(value.OfferId, out var offerId) || !criteria.TryGetValue(value.CriterionId, out var criterionId)) throw new RuleException("InvalidImport");
            evaluation.AssessOffer(offerId, criterionId, new(value.BooleanValue, value.MeasurementValue, value.RatingValue, value.Source, value.Notes, value.OptionalAvailable));
            evaluation.OfferAssessments.Single(assessment => assessment.OfferId == offerId && assessment.CriterionId == criterionId).RestoreRevision(evaluation.FindOffer(offerId), value.RevisionNumber);
        }
        foreach (var offer in offers.Where(offer => offer.Archived)) evaluation.ArchiveOffer(offerIds[offer.Id], true);
        if (content.CostPlanning is { } plan) evaluation.SetCostPlanning(plan.Start, plan.Months);
        var costIds = new Dictionary<Guid, Guid>();
        foreach (var cost in costs)
        {
            Guid? candidateId = null;
            if (cost.Input.CandidateId is { } original)
            {
                if (!candidateIds.TryGetValue(original, out var mapped)) throw new RuleException("InvalidImport");
                candidateId = mapped;
            }
            costIds.Add(cost.Id, evaluation.AddCost(cost.Input with { CandidateId = candidateId }));
        }
        if (content.CalculationSettings is { } calculation) evaluation.SetCalculationSettings(calculation.AnnualReturnPercent);
        foreach (var scenario in scenarios)
        {
            if (!offerIds.TryGetValue(scenario.Input.OfferId, out var offerId)) throw new RuleException("InvalidImport");
            var scenarioId = evaluation.AddScenario(scenario.Input with { OfferId = offerId });
            foreach (var residual in scenario.Residuals) evaluation.SetResidual(scenarioId, residual);
            foreach (var adjustment in scenario.CostAdjustments)
            {
                if (!costIds.TryGetValue(adjustment.CostId, out var costId)) throw new RuleException("InvalidImport");
                evaluation.SetCostAdjustment(scenarioId, adjustment with { CostId = costId });
            }
        }
        return (evaluation, offerIds);
    }
}
