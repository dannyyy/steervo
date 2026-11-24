using System.Globalization;
using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations;

/// <summary>
/// The input fields of a criterion, prefilled from <paramref name="Criterion"/> when one is
/// edited. <paramref name="Prefix"/> keeps the field ids unique on a page with several editors.
/// </summary>
public sealed record CriterionFields(
    IReadOnlyList<CriterionGroup> Groups,
    Criterion? Criterion,
    string Prefix
)
{
    // A new criterion starts with these choices preselected.
    public Priority SelectedPriority => Criterion?.Priority ?? Priority.Required;

    public CriterionScope SelectedScope => Criterion?.Scope ?? CriterionScope.Vehicle;

    public CriterionType SelectedType => Criterion?.Type ?? CriterionType.Boolean;

    public Comparison SelectedComparison => Criterion?.Comparison ?? Comparison.AtLeast;

    /// <summary>The threshold in the culture-independent form a number input expects.</summary>
    public string? ThresholdValue => Criterion?.Threshold?.ToString(CultureInfo.InvariantCulture);

    public IEnumerable<CriterionGroup> GroupsInOrder => Groups.OrderBy(group => group.Position);
}
