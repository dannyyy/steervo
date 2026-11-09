using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;
using Steervo.Application.Accounts;
using Steervo.Core;
using Steervo.Infrastructure.Identity;

namespace Steervo.Web.Pages.Auth;

[EnableRateLimiting("authentication")]
public sealed class LoginModel(IAccountAccess accounts, SignInManager<AppUser> signIn, IStringLocalizer<Ui> text) : PageModel
{
    [BindProperty] public string Email { get; set; } = "";
    public async Task<IActionResult> OnGetAsync()
    {
        if (!await accounts.IsInitializedAsync()) return RedirectToPage("Setup");
        return User.Identity?.IsAuthenticated == true ? RedirectToPage("/Index") : Page();
    }
    public async Task<IActionResult> OnPostAsync()
    {
        try { var id = await accounts.RequestCodeAsync(Email); return RedirectToPage("Verify", new { id }); }
        catch (RuleException error) { ModelState.AddModelError("", text[error.Message]); return Page(); }
    }
    public async Task<IActionResult> OnPostOptionsAsync() => Content(await signIn.MakePasskeyRequestOptionsAsync(null), "application/json");
    public async Task<IActionResult> OnPostPasskeyAsync(string credential)
    {
        if (string.IsNullOrWhiteSpace(credential) || credential.Length > 100_000) return BadRequest();
        try
        {
            var result = await signIn.PasskeySignInAsync(credential);
            return result.Succeeded ? new JsonResult(new { redirect = "/" }) : new JsonResult(new { error = text["PasskeyFailed"].Value }) { StatusCode = 400 };
        }
        catch (Exception error) when (error is System.Text.Json.JsonException or FormatException or InvalidOperationException)
        { return new JsonResult(new { error = text["PasskeyFailed"].Value }) { StatusCode = 400 }; }
    }
}
