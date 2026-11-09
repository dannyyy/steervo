using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Steervo.Application.Accounts;
using Steervo.Application.Evaluations;
using Steervo.Core;
namespace Steervo.Web.Pages.Account;

public sealed class InvitationsModel(IAccountAccess accounts, ICurrentUser current, IStringLocalizer<Ui> text) : PageModel
{
    public IReadOnlyList<InvitationView> Invitations { get; private set; } = [];
    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Name { get; set; } = "";
    public string? Notice { get; private set; }
    public async Task<IActionResult> OnGetAsync()
    {
        if (!await accounts.IsPrimaryAsync(current.Id)) return StatusCode(403);
        Invitations = await accounts.InvitationsAsync(current.Id); return Page();
    }
    public async Task<IActionResult> OnPostAsync(string? action, Guid id)
    {
        if (!await accounts.IsPrimaryAsync(current.Id)) return StatusCode(403);
        try
        {
            if (action == "revoke") await accounts.RevokeInvitationAsync(current.Id, id);
            else await accounts.InviteAsync(current.Id, Email, Name);
            Notice = text[action == "revoke" ? "InvitationRevoked" : "InvitationSent"];
        }
        catch (RuleException error) { ModelState.AddModelError("", text[error.Message]); }
        return await OnGetAsync();
    }
}
