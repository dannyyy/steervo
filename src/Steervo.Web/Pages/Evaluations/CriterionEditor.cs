using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations;

/// <summary>
/// The editor for one criterion of the workspace; without a criterion it adds a new one.
/// </summary>
public sealed record CriterionEditor(DetailsModel Workspace, Criterion? Criterion)
{
    private const string RemoveAction = "remove-criterion";

    public bool IsNew => Criterion is null;

    /// <summary>Distinguishes the element ids of the editors rendered on one page.</summary>
    public string Prefix => Criterion?.Id.ToString() ?? "new";

    public string Action => IsNew ? "add-criterion" : "edit-criterion";

    public string TitleId => IsNew ? "new-criterion-title" : "criterion-title-" + Prefix;

    /// <summary>The values of a failed submission of this editor, to show them again.</summary>
    public string? Draft => Workspace.DraftFor(Action, Criterion?.Id ?? default);

    /// <summary>True when saving or removing through this editor failed, so it stays open.</summary>
    public bool Failed
    {
        get
        {
            if (Draft is not null)
            {
                return true;
            }

            return Criterion is not null
                && Workspace.FailedAction == RemoveAction
                && Workspace.FailedItemId == Criterion.Id;
        }
    }
}
