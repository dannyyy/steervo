using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;
using Steervo.Application.Accounts;
using Steervo.Core;
using Steervo.Infrastructure.Identity;

namespace Steervo.Web.Pages.Auth;

/// <summary>Checks the one-time code from the e-mail and signs the person in.</summary>
[EnableRateLimiting("authentication")]
public sealed class VerifyModel(
    IAccountAccess accounts,
    UserManager<AppUser> users,
    SignInManager<AppUser> signIn,
    IStringLocalizer<Ui> text
) : PageModel
{
    private const string HtmxRequestHeader = "HX-Request";

    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    [BindProperty]
    public string Code { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            var userId = await accounts.VerifyCodeAsync(Id, Code);
            var user =
                await users.FindByIdAsync(userId) ?? throw new RuleException("InvalidAccount");

            await signIn.SignInAsync(user, false, "email-code");

            // Only a local address is accepted as the place to continue after signing in.
            var url = Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/";

            if (Request.Headers.ContainsKey(HtmxRequestHeader))
            {
                Response.Headers["HX-Redirect"] = url;

                return new EmptyResult();
            }

            return LocalRedirect(url);
        }
        catch (RuleException error)
        {
            Response.StatusCode = 422;

            if (Request.Headers.ContainsKey(HtmxRequestHeader))
            {
                return new JsonResult(new { error = text[error.Message].Value });
            }

            ModelState.AddModelError("", text[error.Message]);

            return Page();
        }
    }
}
