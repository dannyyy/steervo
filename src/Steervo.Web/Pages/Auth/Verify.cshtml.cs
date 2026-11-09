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
public sealed class VerifyModel(IAccountAccess accounts, UserManager<AppUser> users, SignInManager<AppUser> signIn, IStringLocalizer<Ui> text) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty] public string Code { get; set; } = "";
    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            var userId = await accounts.VerifyCodeAsync(Id, Code);
            var user = await users.FindByIdAsync(userId) ?? throw new RuleException("InvalidAccount");
            await signIn.SignInAsync(user, false, "email-code");
            return RedirectToPage("/Index");
        }
        catch (RuleException error) { ModelState.AddModelError("", text[error.Message]); return Page(); }
    }
}
