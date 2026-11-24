using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations.Candidates;

/// <summary>The vehicle requirements of one criterion group; a null group holds the ungrouped ones.</summary>
public sealed record VehicleRequirementGroup(
    CriterionGroup? Group,
    IReadOnlyList<Criterion> Criteria
);

/// <summary>The requirements of an evaluation that are assessed per car rather than per offer.</summary>
public static class VehicleRequirements
{
    public static IEnumerable<Criterion> Of(Evaluation evaluation) =>
        evaluation.Criteria.Where(criterion => criterion.Scope == CriterionScope.Vehicle);

    /// <summary>
    /// The requirements by group in display order, ungrouped ones last; groups without a vehicle
    /// requirement are left out.
    /// </summary>
    public static IReadOnlyList<VehicleRequirementGroup> Grouped(Evaluation evaluation)
    {
        var groups = evaluation
            .Groups.OrderBy(group => group.Position)
            .Cast<CriterionGroup?>()
            .Append(null);

        var result = new List<VehicleRequirementGroup>();

        foreach (var group in groups)
        {
            var criteria = Of(evaluation)
                .Where(criterion => criterion.GroupId == group?.Id)
                .OrderBy(criterion => criterion.Position)
                .ToList();

            if (criteria.Count > 0)
            {
                result.Add(new VehicleRequirementGroup(group, criteria));
            }
        }

        return result;
    }
}
