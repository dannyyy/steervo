using System.Text.Json;
using System.Text.Json.Serialization;
using Steervo.Core;

namespace Steervo.Application.Evaluations;

public sealed record GroupExport(Guid Id, string Name, int Position);
public sealed record CriterionExport(Guid Id, string Name, CriterionType Type, Priority Priority, CriterionScope Scope, Guid? GroupId, decimal? Threshold, string? Unit, Comparison Comparison, int Position);
public sealed record EvaluationExport(string Format, int Version, string Name, string Purpose, GroupExport[] Groups, CriterionExport[] Criteria);

public sealed class EvaluationTransfer(EvaluationWorkspace workspace, IEvaluationStore store, ICurrentUser current)
{
    public const int MaximumBytes = 256_000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true, MaxDepth = 16, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    public async Task<byte[]> ExportAsync(Guid id)
    {
        var evaluation = await workspace.ReadAsync(id);
        var content = new EvaluationExport("steervo-evaluation", 1, evaluation.Name, evaluation.Purpose,
            evaluation.Groups.OrderBy(x => x.Position).Select(x => new GroupExport(x.Id, x.Name, x.Position)).ToArray(),
            evaluation.Criteria.OrderBy(x => x.Position).Select(x => new CriterionExport(x.Id, x.Name, x.Type, x.Priority, x.Scope, x.GroupId, x.Threshold, x.Unit, x.Comparison, x.Position)).ToArray());
        return JsonSerializer.SerializeToUtf8Bytes(content, Json);
    }
    public async Task<Guid> ImportAsync(byte[] data)
    {
        if (data.Length > MaximumBytes) throw new RuleException("InvalidImport");
        EvaluationExport content;
        try { content = JsonSerializer.Deserialize<EvaluationExport>(data, Json) ?? throw new RuleException("InvalidImport"); }
        catch (JsonException) { throw new RuleException("InvalidImport"); }
        if (content.Format != "steervo-evaluation" || content.Version != 1 || content.Groups is null || content.Criteria is null || content.Groups.Length > 100 || content.Criteria.Length > 1000
            || content.Groups.Any(x => x is null || x.Id == Guid.Empty || x.Position < 0) || content.Criteria.Any(x => x is null || x.Id == Guid.Empty || x.Position < 0)
            || content.Groups.DistinctBy(x => x.Id).Count() != content.Groups.Length || content.Criteria.DistinctBy(x => x.Id).Count() != content.Criteria.Length)
            throw new RuleException("InvalidImport");
        var evaluation = new Evaluation(current.Id, content.Name, content.Purpose);
        var groups = content.Groups.OrderBy(x => x.Position).ToDictionary(x => x.Id, x => evaluation.AddGroup(x.Name));
        foreach (var criterion in content.Criteria.OrderBy(x => x.Position))
        {
            Guid? groupId = null;
            if (criterion.GroupId is { } original)
            {
                if (!groups.TryGetValue(original, out var mapped)) throw new RuleException("InvalidImport");
                groupId = mapped;
            }
            evaluation.AddCriterion(new(criterion.Name, criterion.Type, criterion.Priority, criterion.Scope, groupId, criterion.Threshold, criterion.Unit, criterion.Comparison));
        }
        await store.CreateAsync(evaluation);
        return evaluation.Id;
    }
}
