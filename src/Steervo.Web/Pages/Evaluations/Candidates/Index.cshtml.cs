using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Steervo.Application.Evaluations;
using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations.Candidates;

public sealed class IndexModel(CandidateWorkspace workspace, ICurrentUser current, IStringLocalizer<Ui> text) : PageModel
{
    public Evaluation Evaluation { get; private set; } = null!;
    public AccessLevel Access => Evaluation.AccessFor(current.Id)!.Value;
    public string? Notice { get; private set; }
    public bool Failed { get; private set; }
    public string? FailedAction { get; private set; }
    public Guid? FailedItemId { get; private set; }
    public string? CopyName { get; private set; }
    public bool CreationFailed { get; private set; }
    [BindProperty] public CandidateForm Input { get; set; } = new();
    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        try { Evaluation = await workspace.ReadAsync(id); return Page(); }
        catch (RuleException error) { return StatusCode(error.Message == "Forbidden" ? 403 : 404); }
    }
    public async Task<IActionResult> OnPostAsync(Guid id, long version, string action, Guid itemId, int direction, string? copyName)
    {
        try
        {
            (await workspace.ReadAsync(id)).Require(current.Id, AccessLevel.Editor);
            if (!ModelState.IsValid) throw new RuleException("InvalidValue");
            switch (action)
            {
                case "add-candidate":
                    var candidateId = await workspace.AddAsync(id, version, Input.ToInput());
                    if (Request.Headers.ContainsKey("HX-Request"))
                    {
                        Response.Headers["HX-Redirect"] = Url.Page("Details", new { id, candidateId });
                        return new EmptyResult();
                    }
                    return RedirectToPage("Details", new { id, candidateId });
                case "move-candidate": await workspace.MoveAsync(id, version, itemId, direction); break;
                case "remove-candidate": await workspace.RemoveAsync(id, version, itemId); break;
                case "copy-candidate":
                    var copyId = await workspace.CopyAsync(id, version, itemId, copyName ?? "");
                    if (Request.Headers.ContainsKey("HX-Request"))
                    {
                        Response.Headers["HX-Redirect"] = Url.Page("Details", new { id, candidateId = copyId });
                        return new EmptyResult();
                    }
                    return RedirectToPage("Details", new { id, candidateId = copyId });
                default: throw new RuleException("InvalidValue");
            }
            Notice = text["Saved"];
        }
        catch (RuleException error)
        {
            if (error.Message is "Forbidden" or "NotFound") return StatusCode(error.Message == "Forbidden" ? 403 : 404);
            Failed = true; Notice = text[error.Message]; CreationFailed = action == "add-candidate";
            FailedAction = action; FailedItemId = itemId; CopyName = copyName;
            Response.StatusCode = error.Message == "Conflict" ? 409 : 422;
        }
        Evaluation = await workspace.ReadAsync(id);
        return Request.Headers.ContainsKey("HX-Request") ? Partial("_CandidateList", this) : Page();
    }
}
