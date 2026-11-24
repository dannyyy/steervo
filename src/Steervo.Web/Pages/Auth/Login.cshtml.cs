using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;
using Steervo.Application.Accounts;
using Steervo.Core;
using Steervo.Infrastructure.Identity;

namespace Steervo.Web.Pages.Auth;

/// <summary>Sign-in with a one-time code sent by e-mail or with a passkey.</summary>
[EnableRateLimiting("authentication")]
public sealed class LoginModel(
    IAccountAccess accounts,
    SignInManager<AppUser> signIn,
    IStringLocalizer<Ui> text
) : PageModel
{
    private const int MaximumCredentialLength = 100_000;
    private const string HtmxRequestHeader = "HX-Request";

    [BindProperty]
    public string Email { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    // Only a local address is accepted as the place to continue after signing in.
    private string Destination => Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/";

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await accounts.IsInitializedAsync())
        {
            return RedirectToPage("Setup");
        }

        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(Destination);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            var id = await accounts.RequestCodeAsync(Email);
            var url = Url.Page("Verify", new { id, returnUrl = Destination })!;

            if (Request.Headers.ContainsKey(HtmxRequestHeader))
            {
                Response.Headers["HX-Redirect"] = url;

                return new EmptyResult();
            }

            return Redirect(url);
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

    public async Task<IActionResult> OnPostOptionsAsync()
    {
        var requestOptions = await signIn.MakePasskeyRequestOptionsAsync(null);

        return Content(requestOptions, "application/json");
    }

    public async Task<IActionResult> OnPostPasskeyAsync(string credential)
    {
        if (string.IsNullOrWhiteSpace(credential) || credential.Length > MaximumCredentialLength)
        {
            return BadRequest();
        }

        try
        {
            var result = await signIn.PasskeySignInAsync(credential);

            if (!result.Succeeded)
            {
                return PasskeyFailed();
            }

            return new JsonResult(new { redirect = Destination });
        }
        catch (Exception error)
            when (error
                    is System.Text.Json.JsonException
                        or FormatException
                        or InvalidOperationException
            )
        {
            // A malformed credential is answered like a rejected one.
            return PasskeyFailed();
        }
    }

    private JsonResult PasskeyFailed()
    {
        return new JsonResult(new { error = text["PasskeyFailed"].Value }) { StatusCode = 400 };
    }
}
