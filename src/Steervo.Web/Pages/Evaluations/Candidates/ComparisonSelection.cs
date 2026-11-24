using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations.Candidates;

/// <summary>The cars shown side by side, carried in the query as first, second and any further "more" values.</summary>
public static class ComparisonSelection
{
    public const int Maximum = 5;

    // The first and the second car have their own query value; only the rest travels as "more".
    private const int NamedSlots = 2;

    /// <summary>The query string for the given cars; missing slots stay in the query with an empty value.</summary>
    public static string Query(IEnumerable<Guid> ids)
    {
        var selected = ids.ToList();
        Guid? first = selected.Count > 0 ? selected[0] : null;
        Guid? second = selected.Count > 1 ? selected[1] : null;

        return $"first={first}&second={second}{More(selected.Skip(NamedSlots))}";
    }

    public static string More(IEnumerable<Guid>? ids) =>
        string.Concat((ids ?? []).Select(id => $"&more={id}"));

    /// <summary>Further cars of a selection that still exist and are not already its first or second car.</summary>
    public static IReadOnlyList<Guid> Further(
        Evaluation evaluation,
        Guid? first,
        Guid? second,
        IEnumerable<Guid>? more
    )
    {
        bool IsFurtherCar(Guid id)
        {
            var alreadyNamed = id == first || id == second;
            return !alreadyNamed && evaluation.Candidates.Any(candidate => candidate.Id == id);
        }

        return (more ?? []).Distinct().Where(IsFurtherCar).Take(Maximum - NamedSlots).ToList();
    }
}
