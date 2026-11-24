using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations;

/// <summary>
/// The editor for one person's access to the evaluation. The user id "new" stands for the
/// editor that grants access to somebody who has none yet.
/// </summary>
public sealed record ShareEditor(
    DetailsModel Workspace,
    string UserId,
    string Email,
    AccessLevel Access
)
{
    private const string NewUserId = "new";
    private const string ShareAction = "share";

    public bool IsNew => UserId == NewUserId;

    /// <summary>The id of the button that opens this editor and regains focus on close.</summary>
    public string OpenerId => IsNew ? "add-share" : "edit-share-" + UserId;

    /// <summary>
    /// All share editors post the same action; the submitted editor id tells which one failed.
    /// </summary>
    public bool Failed =>
        Workspace.FailedAction == ShareAction && Workspace.Request.Form["editorId"] == UserId;

    /// <summary>The values of a failed submission of this editor, to show them again.</summary>
    public string? Draft => Failed ? Workspace.SubmittedValues : null;
}
