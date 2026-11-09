using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Steervo.Infrastructure.Identity;
namespace Steervo.Web.Pages.Account;
public sealed class LogoutModel(SignInManager<AppUser> signIn) : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Index");
    public async Task<IActionResult> OnPostAsync() { await signIn.SignOutAsync(); return RedirectToPage("/Auth/Login"); }
}
