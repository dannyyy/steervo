using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations.Candidates;

/// <summary>The cars shown side by side, carried in the query as first, second and any further "more" values.</summary>
public static class ComparisonSelection
{
    public const int Maximum = 5;

    public static string Query(IEnumerable<Guid> ids)
    {
        var list = ids.ToList();
        return $"first={(list.Count > 0 ? list[0] : null)}&second={(list.Count > 1 ? list[1] : null)}{More(list.Skip(2))}";
    }

    public static string More(IEnumerable<Guid>? ids) => string.Concat((ids ?? []).Select(id => $"&more={id}"));

    /// <summary>Further cars of a selection that still exist and are not already its first or second car.</summary>
    public static IReadOnlyList<Guid> Further(Evaluation evaluation, Guid? first, Guid? second, IEnumerable<Guid>? more) =>
        (more ?? []).Distinct().Where(id => id != first && id != second && evaluation.Candidates.Any(candidate => candidate.Id == id))
            .Take(Maximum - 2).ToList();
}
