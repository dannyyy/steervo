using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations;

/// <summary>
/// The editor for one criterion group of the workspace; without a group it adds a new one.
/// </summary>
public sealed record GroupEditor(DetailsModel Workspace, CriterionGroup? Group)
{
    private const string RemoveAction = "remove-group";

    public bool IsNew => Group is null;

    /// <summary>Distinguishes the element ids of the editors rendered on one page.</summary>
    public string Prefix => Group?.Id.ToString() ?? "new";

    public string Action => IsNew ? "add-group" : "rename-group";

    public string EditorId => IsNew ? "new-group-editor" : "group-editor-" + Prefix;

    /// <summary>The id of the button that opens this editor and regains focus on close.</summary>
    public string OpenerId => IsNew ? "add-group" : "edit-group-" + Prefix;

    /// <summary>The values of a failed submission of this editor, to show them again.</summary>
    public string? Draft => Workspace.DraftFor(Action, Group?.Id ?? default);

    /// <summary>True when saving or removing through this editor failed, so it stays open.</summary>
    public bool Failed
    {
        get
        {
            if (Draft is not null)
            {
                return true;
            }

            return Group is not null
                && Workspace.FailedAction == RemoveAction
                && Workspace.FailedItemId == Group.Id;
        }
    }
}
