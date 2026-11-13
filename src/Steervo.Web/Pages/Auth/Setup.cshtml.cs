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
        try
        {
            var id = await accounts.BeginSetupAsync(Token, Email, Name);
            var url = Url.Page("Verify", new { id })!;
            if (Request.Headers.ContainsKey("HX-Request")) { Response.Headers["HX-Redirect"] = url; return new EmptyResult(); }
            return Redirect(url);
        }
        catch (RuleException error)
        {
            Response.StatusCode = 422;
            if (Request.Headers.ContainsKey("HX-Request")) return new JsonResult(new { error = text[error.Message].Value });
            ModelState.AddModelError("", text[error.Message]); return Page();
        }
    }
}
