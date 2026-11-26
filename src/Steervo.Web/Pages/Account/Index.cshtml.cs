using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Localization;
using Steervo.Core;
using Steervo.Infrastructure.Identity;

namespace Steervo.Web.Pages.Account;

/// <summary>
/// The signed-in person's account: lists the passkeys and lets them add, rename or remove one.
/// Every change requires a recent sign-in.
/// </summary>
public sealed class IndexModel(
    UserManager<AppUser> users,
    SignInManager<AppUser> signIn,
    IStringLocalizer<Ui> text,
    ILogger<IndexModel> logger
) : PageModel
{
    private const int MaximumPasskeys = 10;
    private const int MaximumNameLength = 80;
    private const int MaximumCredentialLength = 100_000;
    private const string HtmxRequestHeader = "HX-Request";
    private const string RemoveAction = "remove";
    private const string RenameAction = "rename";

    // Passkeys may only be changed for this long after signing in.
    private static readonly TimeSpan RecentSignInWindow = TimeSpan.FromMinutes(15);

    public IList<UserPasskeyInfo> Passkeys { get; private set; } = [];

    public bool Recent { get; private set; }

    [TempData]
    public string? Notice { get; set; }

    public string AccountName { get; private set; } = "";

    public string Email { get; private set; } = "";

    /// <summary>The passkey whose rename was rejected, so that its editor opens again.</summary>
    public string? FailedId { get; private set; }

    public string? DraftName { get; private set; }

    /// <summary>The value a form posts back as <c>id</c> to address this passkey.</summary>
    public string CredentialOf(UserPasskeyInfo passkey)
    {
        return WebEncoders.Base64UrlEncode(passkey.CredentialId);
    }

    /// <summary>The suffix of the element ids that belong to this passkey.</summary>
    public string AnchorOf(UserPasskeyInfo passkey)
    {
        return Convert.ToHexString(passkey.CredentialId);
    }

    public async Task OnGetAsync()
    {
        var user = await CurrentAsync();

        AccountName = user.Name;
        Email = user.Email!;
        Passkeys = await users.GetPasskeysAsync(user);
        Recent = await IsRecentAsync();
    }

    public async Task<IActionResult> OnPostOptionsAsync()
    {
        if (!await IsRecentAsync())
        {
            return RecentSignInRequired();
        }

        var user = await CurrentAsync();

        if (await HasReachedPasskeyLimitAsync(user))
        {
            return new JsonResult(new { error = text["PasskeyLimit"].Value }) { StatusCode = 400 };
        }

        var creationOptions = await signIn.MakePasskeyCreationOptionsAsync(
            new PasskeyUserEntity
            {
                Id = user.Id,
                Name = user.Email!,
                DisplayName = user.Name,
            }
        );

        return Content(creationOptions, "application/json");
    }

    public async Task<IActionResult> OnPostEnrollAsync(string credential, string name)
    {
        if (!await IsRecentAsync())
        {
            return RecentSignInRequired();
        }

        if (string.IsNullOrWhiteSpace(credential) || credential.Length > MaximumCredentialLength)
        {
            return BadRequest();
        }

        try
        {
            var passkey = await EnrollPasskeyAsync(credential, name);

            Notice = text["PasskeyAdded"];

            return new JsonResult(new { redirect = "/Account#passkey-" + AnchorOf(passkey) });
        }
        catch (Exception error)
            when (error
                    is RuleException
                        or System.Text.Json.JsonException
                        or FormatException
                        or InvalidOperationException
            )
        {
            if (error is not RuleException)
            {
                logger.LogWarning(
                    "Passkey registration stopped with {ErrorType}: {Reason}. Request {RequestId}",
                    error.GetType().Name,
                    error.Message,
                    HttpContext.TraceIdentifier
                );
            }

            var ruleKey = error is RuleException ? error.Message : "PasskeyFailed";

            return new JsonResult(new { error = text[MessageKeyFor(ruleKey)].Value })
            {
                StatusCode = 400,
            };
        }
    }

    public async Task<IActionResult> OnPostAsync(string action, string id, string? name)
    {
        if (!await IsRecentAsync())
        {
            return RecentSignInRequired();
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest();
        }

        try
        {
            var user = await CurrentAsync();
            var credentialId = WebEncoders.Base64UrlDecode(id);
            var passkey = await users.GetPasskeyAsync(user, credentialId)
                ?? throw new RuleException("NotFound");

            var result = await ChangePasskeyAsync(user, passkey, credentialId, action, name);

            if (!result.Succeeded)
            {
                throw new RuleException("Conflict");
            }

            if (action == RemoveAction)
            {
                // A new security stamp ends all other sessions; the current one is renewed.
                await users.UpdateSecurityStampAsync(user);
                await signIn.RefreshSignInAsync(user);
            }

            Notice = text["Saved"];

            var anchor =
                action == RemoveAction
                    ? "passkeys-title"
                    : "passkey-" + Convert.ToHexString(credentialId);

            return RedirectAfterChange("/Account#" + anchor);
        }
        catch (RuleException error)
        {
            Response.StatusCode = StatusCodeFor(error);
            var message = text[MessageKeyFor(error.Message)].Value;

            if (Request.Headers.ContainsKey(HtmxRequestHeader))
            {
                return new JsonResult(new { error = message });
            }

            ModelState.AddModelError("", message);
            FailedId = id;
            DraftName = name;
        }
        catch (FormatException)
        {
            return BadRequest();
        }

        await OnGetAsync();

        return Page();
    }

    private async Task<AppUser> CurrentAsync()
    {
        return await users.GetUserAsync(User) ?? throw new UnauthorizedAccessException();
    }

    private async Task<bool> IsRecentAsync()
    {
        var session = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);

        return session.Properties?.IssuedUtc > DateTimeOffset.UtcNow.Subtract(RecentSignInWindow);
    }

    private JsonResult RecentSignInRequired()
    {
        return new JsonResult(new { error = text["RecentSignInRequired"].Value })
        {
            StatusCode = 403,
        };
    }

    private async Task<bool> HasReachedPasskeyLimitAsync(AppUser user)
    {
        var passkeys = await users.GetPasskeysAsync(user);

        return passkeys.Count >= MaximumPasskeys;
    }

    /// <summary>
    /// Verifies the browser's attestation and stores the new passkey under the given name.
    /// Every rejection is logged with its reason and surfaces as a <see cref="RuleException"/>.
    /// </summary>
    private async Task<UserPasskeyInfo> EnrollPasskeyAsync(string credential, string name)
    {
        name = Rules.Text(name, MaximumNameLength);
        var user = await CurrentAsync();

        if (await HasReachedPasskeyLimitAsync(user))
        {
            throw new RuleException("PasskeyLimit");
        }

        var result = await signIn.PerformPasskeyAttestationAsync(credential);

        if (!result.Succeeded)
        {
            logger.LogWarning(
                "Passkey registration rejected: {Reason}. Request {RequestId}",
                result.Failure.Message,
                HttpContext.TraceIdentifier
            );

            throw new RuleException("PasskeyFailed");
        }

        if (result.UserEntity.Id != user.Id)
        {
            logger.LogWarning(
                "Passkey registration account mismatch. Request {RequestId}",
                HttpContext.TraceIdentifier
            );

            throw new RuleException("PasskeyFailed");
        }

        result.Passkey.Name = name;
        var saved = await users.AddOrUpdatePasskeyAsync(user, result.Passkey);

        if (!saved.Succeeded)
        {
            logger.LogWarning(
                "Passkey registration could not be stored: {ErrorCodes}. Request {RequestId}",
                string.Join(", ", saved.Errors.Select(error => error.Code)),
                HttpContext.TraceIdentifier
            );

            throw new RuleException("Conflict");
        }

        return result.Passkey;
    }

    private async Task<IdentityResult> ChangePasskeyAsync(
        AppUser user,
        UserPasskeyInfo passkey,
        byte[] credentialId,
        string action,
        string? name
    )
    {
        if (action == RemoveAction)
        {
            return await users.RemovePasskeyAsync(user, credentialId);
        }

        if (action == RenameAction)
        {
            passkey.Name = Rules.Text(name, MaximumNameLength);

            return await users.AddOrUpdatePasskeyAsync(user, passkey);
        }

        throw new RuleException("InvalidValue");
    }

    /// <summary>HTMX follows the redirect header; a plain form post gets a regular redirect.</summary>
    private IActionResult RedirectAfterChange(string url)
    {
        if (Request.Headers.ContainsKey(HtmxRequestHeader))
        {
            Response.Headers["HX-Redirect"] = url;

            return new EmptyResult();
        }

        return Redirect(url);
    }

    private static int StatusCodeFor(RuleException error)
    {
        return error.Message switch
        {
            "NotFound" => 404,
            "Conflict" => 409,
            _ => 422,
        };
    }

    /// <summary>A storage conflict gets the account-specific wording; other keys are used as is.</summary>
    private static string MessageKeyFor(string ruleKey)
    {
        return ruleKey == "Conflict" ? "AccountChangeConflict" : ruleKey;
    }
}
