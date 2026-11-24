using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Steervo.Infrastructure.Identity;

namespace Steervo.Web.Pages.Account;

/// <summary>Ends the session. Signing out only happens by POST; a GET just leads home.</summary>
public sealed class LogoutModel(SignInManager<AppUser> signIn) : PageModel
{
    public IActionResult OnGet()
    {
        return RedirectToPage("/Index");
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl)
    {
        await signIn.SignOutAsync();

        // Only a local address may be carried over to the sign-in page.
        var safeReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null;

        return RedirectToPage("/Auth/Login", new { returnUrl = safeReturnUrl });
    }
}
