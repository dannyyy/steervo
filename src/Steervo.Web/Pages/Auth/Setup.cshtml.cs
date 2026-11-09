using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;
using Steervo.Application.Accounts;
using Steervo.Core;

namespace Steervo.Web.Pages.Auth;

[EnableRateLimiting("authentication")]
public sealed class SetupModel(IAccountAccess accounts, IStringLocalizer<Ui> text) : PageModel
{
    [BindProperty] public string Token { get; set; } = "";
    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Name { get; set; } = "";
    public async Task<IActionResult> OnGetAsync() => await accounts.IsInitializedAsync() ? NotFound() : Page();
    public async Task<IActionResult> OnPostAsync()
    {
        if (await accounts.IsInitializedAsync()) return NotFound();
        try { var id = await accounts.BeginSetupAsync(Token, Email, Name); return RedirectToPage("Verify", new { id }); }
        catch (RuleException error) { ModelState.AddModelError("", text[error.Message]); return Page(); }
    }
}
