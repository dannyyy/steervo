using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Steervo.Application.Documents;
using Steervo.Application.Evaluations;
using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations;

/// <summary>
/// The evaluation workspace: general settings, criteria and their groups, sharing and data
/// transfer. Every change is posted as a named action against the evaluation version the form
/// was rendered with.
/// </summary>
public sealed class DetailsModel(
    EvaluationWorkspace workspace,
    EvaluationArchive transfer,
    ICurrentUser current,
    IAccountDirectory accounts,
    IStringLocalizer<Ui> text
) : PageModel
{
    private const string SettingsSection = "settings";
    private const string CriteriaSection = "criteria";
    private const string SharingSection = "sharing";

    private const string ForbiddenError = "Forbidden";
    private const string NotFoundError = "NotFound";
    private const string ConflictError = "Conflict";
    private const string InvalidValueError = "InvalidValue";

    private const string HtmxRequestHeader = "HX-Request";
    private const string HtmxRedirectHeader = "HX-Redirect";

    /// <summary>Form fields that are never echoed back into a failed editor.</summary>
    private const string AntiforgeryField = "__RequestVerificationToken";
    private const string VersionField = "version";

    /// <summary>
    /// Actions without an editor of their own. When one of them fails, the message appears at
    /// the top of the workspace instead of inside an open editor.
    /// </summary>
    private static readonly string[] ActionsWithoutEditor =
    [
        "move-group",
        "place-group",
        "move-criterion",
        "place-criterion",
        "delete",
        "revoke",
    ];

    public Evaluation Evaluation { get; private set; } = null!;

    public AccessLevel Access => Evaluation.AccessFor(current.Id)!.Value;

    public bool CanEdit => Access >= AccessLevel.Editor;

    public bool IsOwner => Access == AccessLevel.Owner;

    public IReadOnlyDictionary<string, string> ShareEmails { get; private set; } =
        new Dictionary<string, string>();

    public string? Notice { get; private set; }

    public bool Failed { get; private set; }

    [BindProperty(SupportsGet = true, Name = "section")]
    public string? RequestedSection { get; set; }

    public string Section { get; private set; } = SettingsSection;

    public string? FailedAction { get; private set; }

    public Guid FailedItemId { get; private set; }

    public string? SubmittedValues { get; private set; }

    /// <summary>
    /// True when the notice belongs at the top of the workspace: every success, and failures of
    /// actions that have no editor to show the message in.
    /// </summary>
    public bool ShowsWorkspaceNotice
    {
        get
        {
            if (Notice is null)
            {
                return false;
            }

            if (!Failed || FailedAction is null)
            {
                return true;
            }

            return ActionsWithoutEditor.Contains(FailedAction);
        }
    }

    /// <summary>
    /// The values submitted with a failed action, so that its editor can show them again; null
    /// when the given editor is not the one that failed.
    /// </summary>
    public string? DraftFor(string action, Guid itemId = default) =>
        FailedAction == action && FailedItemId == itemId ? SubmittedValues : null;

    public List<Criterion> CriteriaIn(CriterionScope scope)
    {
        return Evaluation.Criteria.Where(criterion => criterion.Scope == scope).ToList();
    }

    /// <summary>
    /// The groups listed under a scope, in their order. A group appears where it has criteria;
    /// a group without any criteria is listed with the vehicle criteria.
    /// </summary>
    public List<CriterionGroup> GroupsListedIn(CriterionScope scope, List<Criterion> scoped)
    {
        return Evaluation
            .Groups.OrderBy(group => group.Position)
            .Where(group =>
                scoped.Any(criterion => criterion.GroupId == group.Id)
                || scope == CriterionScope.Vehicle
                    && Evaluation.Criteria.All(criterion => criterion.GroupId != group.Id)
            )
            .ToList();
    }

    /// <summary>
    /// A group holding both kinds of criteria is listed twice; only its first listing, under
    /// the vehicle criteria, can be renamed or removed.
    /// </summary>
    public bool IsRepeatedListing(CriterionGroup? group, CriterionScope scope)
    {
        return group is not null
            && scope == CriterionScope.Offer
            && Evaluation.Criteria.Any(criterion =>
                criterion.GroupId == group.Id && criterion.Scope == CriterionScope.Vehicle
            );
    }

    /// <summary>The criteria of one listing in their order; a null group is "ungrouped".</summary>
    public List<Criterion> CriteriaOf(CriterionGroup? group, List<Criterion> scoped)
    {
        return scoped
            .Where(criterion => criterion.GroupId == group?.Id)
            .OrderBy(criterion => criterion.Position)
            .ToList();
    }

    /// <summary>
    /// Keeps element ids unique: the ungrouped criteria and a repeated group are listed under
    /// both scopes, so their listing under the offer criteria carries a suffix.
    /// </summary>
    public string ListingIdSuffix(CriterionGroup? group, CriterionScope scope)
    {
        var listedTwice = group is null || IsRepeatedListing(group, scope);

        return scope == CriterionScope.Offer && listedTwice ? "-offer" : "";
    }

    /// <summary>Counts the criteria of a group across both scopes; null is "ungrouped".</summary>
    public int CriterionCountIn(CriterionGroup? group)
    {
        return Evaluation.Criteria.Count(criterion => criterion.GroupId == group?.Id);
    }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        try
        {
            await LoadAsync(id);

            return Page();
        }
        catch (RuleException error)
        {
            return StatusCode(AccessFailureStatus(error));
        }
    }

    public async Task<IActionResult> OnGetExportAsync(Guid id)
    {
        try
        {
            var result = await transfer.ExportAsync(id, HttpContext.RequestAborted);

            return File(result.Content, result.ContentType, result.FileName);
        }
        catch (RuleException error)
        {
            if (IsAccessFailure(error))
            {
                return StatusCode(AccessFailureStatus(error));
            }

            await LoadAsync(id);
            Failed = true;
            Notice = text[error.Message];
            Response.StatusCode = 422;

            return Page();
        }
    }

    public async Task<IActionResult> OnPostAsync(
        Guid id,
        long version,
        string action,
        Guid itemId,
        string name,
        string? purpose,
        string? country,
        string? currency,
        Guid? groupId,
        int direction,
        int position,
        CriterionType type,
        Priority priority,
        CriterionScope scope,
        string? threshold,
        string? unit,
        Comparison comparison,
        string email,
        AccessLevel access,
        string userId
    )
    {
        try
        {
            if (!ModelState.IsValid)
            {
                throw new RuleException(InvalidValueError);
            }

            var criterion = new CriterionInput(
                name,
                type,
                priority,
                scope,
                groupId,
                ParseThreshold(threshold),
                unit,
                comparison
            );

            switch (action)
            {
                case "rename":
                    await workspace.EditAsync(
                        id,
                        version,
                        evaluation => evaluation.SetProperties(name, purpose, country, currency)
                    );
                    break;

                case "add-group":
                    await workspace.EditAsync(id, version, evaluation => evaluation.AddGroup(name));
                    break;

                case "rename-group":
                    await workspace.EditAsync(
                        id,
                        version,
                        evaluation => evaluation.RenameGroup(itemId, name)
                    );
                    break;

                case "move-group":
                    await workspace.EditAsync(
                        id,
                        version,
                        evaluation => evaluation.MoveGroup(itemId, direction)
                    );
                    break;

                case "place-group":
                    await workspace.EditAsync(
                        id,
                        version,
                        evaluation => evaluation.PlaceGroup(itemId, position)
                    );
                    break;

                case "remove-group":
                    await workspace.EditAsync(
                        id,
                        version,
                        evaluation => evaluation.RemoveGroup(itemId)
                    );
                    break;

                case "add-criterion":
                    await workspace.EditAsync(
                        id,
                        version,
                        evaluation => evaluation.AddCriterion(criterion)
                    );
                    break;

                case "edit-criterion":
                    await workspace.EditAsync(
                        id,
                        version,
                        evaluation => evaluation.EditCriterion(itemId, criterion)
                    );
                    break;

                case "move-criterion":
                    await workspace.EditAsync(
                        id,
                        version,
                        evaluation => evaluation.MoveCriterion(itemId, direction)
                    );
                    break;

                case "place-criterion":
                    await workspace.EditAsync(
                        id,
                        version,
                        evaluation => evaluation.PlaceCriterion(itemId, groupId, position)
                    );
                    break;

                case "remove-criterion":
                    await workspace.EditAsync(
                        id,
                        version,
                        evaluation => evaluation.RemoveCriterion(itemId)
                    );
                    break;

                case "share":
                    await workspace.ShareAsync(id, version, email, access);
                    break;

                case "revoke":
                    await workspace.RevokeAsync(id, version, userId);
                    break;

                case "delete":
                    await workspace.DeleteAsync(id, version);

                    // The evaluation no longer exists, so there is no workspace left to render.
                    return RedirectToOverview();

                default:
                    throw new RuleException(InvalidValueError);
            }

            Notice = text["Saved"];
        }
        catch (RuleException error)
        {
            if (IsAccessFailure(error))
            {
                return StatusCode(AccessFailureStatus(error));
            }

            RememberFailure(error, action, itemId);
        }

        await LoadAsync(id);

        if (IsHtmxRequest())
        {
            return Partial("_Workspace", this);
        }

        return Page();
    }

    private static bool IsAccessFailure(RuleException error)
    {
        return error.Message is ForbiddenError or NotFoundError;
    }

    private static int AccessFailureStatus(RuleException error)
    {
        return error.Message == ForbiddenError ? 403 : 404;
    }

    /// <summary>An empty threshold means "none"; anything else must be a plain decimal.</summary>
    private static decimal? ParseThreshold(string? threshold)
    {
        if (string.IsNullOrWhiteSpace(threshold))
        {
            return null;
        }

        var isDecimal = decimal.TryParse(
            threshold,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var parsed
        );

        if (!isDecimal)
        {
            throw new RuleException(InvalidValueError);
        }

        return parsed;
    }

    private bool IsHtmxRequest()
    {
        return Request.Headers.ContainsKey(HtmxRequestHeader);
    }

    private IActionResult RedirectToOverview()
    {
        if (IsHtmxRequest())
        {
            Response.Headers[HtmxRedirectHeader] = "/";

            return new EmptyResult();
        }

        return RedirectToPage("/Index");
    }

    /// <summary>
    /// Keeps the message and the submitted values so that the editor of the failed action
    /// reopens with what the user typed.
    /// </summary>
    private void RememberFailure(RuleException error, string action, Guid itemId)
    {
        Failed = true;
        Notice = text[error.Message];
        FailedAction = action;
        FailedItemId = itemId;

        // The token and the version are rendered fresh with the page; a draft must not restore them.
        var submittedFields = Request
            .Form.Where(field => field.Key != AntiforgeryField && field.Key != VersionField)
            .ToDictionary(field => field.Key, field => field.Value.ToString());
        SubmittedValues = JsonSerializer.Serialize(submittedFields);

        Response.StatusCode = error.Message == ConflictError ? 409 : 422;
    }

    private async Task LoadAsync(Guid id)
    {
        Evaluation = await workspace.ReadAsync(id);

        // The evaluation opens on its general page; criteria forms post without a section and
        // stay on the criteria.
        Section = RequestedSection switch
        {
            SharingSection or SettingsSection => SettingsSection,
            CriteriaSection => CriteriaSection,
            _ => HttpMethods.IsPost(Request.Method) ? CriteriaSection : SettingsSection,
        };

        if (IsOwner)
        {
            ShareEmails = await accounts.EmailsAsync(
                Evaluation.Shares.Select(share => share.UserId)
            );
        }
        else
        {
            ShareEmails = new Dictionary<string, string>();
        }
    }
}
