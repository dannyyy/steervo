using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Steervo.Application.Evaluations;
using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations;

public sealed class DetailsModel(EvaluationWorkspace workspace, EvaluationTransfer transfer, ICurrentUser current, IAccountDirectory accounts, IStringLocalizer<Ui> text) : PageModel
{
    public Evaluation Evaluation { get; private set; } = null!;
    public AccessLevel Access => Evaluation.AccessFor(current.Id)!.Value;
    public IReadOnlyDictionary<string, string> ShareNames { get; private set; } = new Dictionary<string, string>();
    public string? Notice { get; private set; }
    public bool Failed { get; private set; }
    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        try { await LoadAsync(id); return Page(); }
        catch (RuleException error) { return StatusCode(error.Message == "Forbidden" ? 403 : 404); }
    }
    public async Task<IActionResult> OnGetExportAsync(Guid id)
    {
        try { return File(await transfer.ExportAsync(id), "application/json", $"evaluation-{id}.json"); }
        catch (RuleException error) { return StatusCode(error.Message == "Forbidden" ? 403 : 404); }
    }
    public async Task<IActionResult> OnPostAsync(Guid id, long version, string action, Guid itemId, string name, string? purpose,
        Guid? groupId, int direction, int position, CriterionType type, Priority priority, CriterionScope scope, string? threshold, string? unit, Comparison comparison, string email, AccessLevel access, string userId)
    {
        try
        {
            if (!ModelState.IsValid) throw new RuleException("InvalidValue");
            decimal? measurementThreshold = null;
            if (!string.IsNullOrWhiteSpace(threshold))
            {
                if (!decimal.TryParse(threshold, System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var parsed)) throw new RuleException("InvalidValue");
                measurementThreshold = parsed;
            }
            switch (action)
            {
                case "rename": await workspace.EditAsync(id, version, e => e.Rename(name, purpose)); break;
                case "add-group": await workspace.EditAsync(id, version, e => e.AddGroup(name)); break;
                case "rename-group": await workspace.EditAsync(id, version, e => e.RenameGroup(itemId, name)); break;
                case "move-group": await workspace.EditAsync(id, version, e => e.MoveGroup(itemId, direction)); break;
                case "place-group": await workspace.EditAsync(id, version, e => e.PlaceGroup(itemId, position)); break;
                case "remove-group": await workspace.EditAsync(id, version, e => e.RemoveGroup(itemId)); break;
                case "add-criterion": await workspace.EditAsync(id, version, e => e.AddCriterion(new(name, type, priority, scope, groupId, measurementThreshold, unit, comparison))); break;
                case "edit-criterion": await workspace.EditAsync(id, version, e => e.EditCriterion(itemId, new(name, type, priority, scope, groupId, measurementThreshold, unit, comparison))); break;
                case "move-criterion": await workspace.EditAsync(id, version, e => e.MoveCriterion(itemId, direction)); break;
                case "place-criterion": await workspace.EditAsync(id, version, e => e.PlaceCriterion(itemId, groupId, position)); break;
                case "remove-criterion": await workspace.EditAsync(id, version, e => e.RemoveCriterion(itemId)); break;
                case "share": await workspace.ShareAsync(id, version, email, access); break;
                case "revoke": await workspace.RevokeAsync(id, version, userId); break;
                case "delete":
                    await workspace.DeleteAsync(id, version);
                    if (Request.Headers.ContainsKey("HX-Request")) { Response.Headers["HX-Redirect"] = "/"; return new EmptyResult(); }
                    return RedirectToPage("/Index");
                default: throw new RuleException("InvalidValue");
            }
            Notice = text["Saved"];
        }
        catch (RuleException error)
        {
            if (error.Message is "Forbidden" or "NotFound") return StatusCode(error.Message == "Forbidden" ? 403 : 404);
            Failed = true; Notice = text[error.Message]; Response.StatusCode = error.Message == "Conflict" ? 409 : 422;
        }
        await LoadAsync(id);
        return Request.Headers.ContainsKey("HX-Request") ? Partial("_Workspace", this) : Page();
    }
    private async Task LoadAsync(Guid id)
    {
        Evaluation = await workspace.ReadAsync(id);
        ShareNames = Access == AccessLevel.Owner ? await accounts.NamesAsync(Evaluation.Shares.Select(x => x.UserId)) : new Dictionary<string, string>();
    }
}
