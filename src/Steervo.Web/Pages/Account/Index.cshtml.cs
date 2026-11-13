using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Localization;
using Steervo.Core;
using Steervo.Infrastructure.Identity;

namespace Steervo.Web.Pages.Account;

public sealed class IndexModel(UserManager<AppUser> users, SignInManager<AppUser> signIn, IStringLocalizer<Ui> text, ILogger<IndexModel> logger) : PageModel
{
    public IList<UserPasskeyInfo> Passkeys { get; private set; } = [];
    public bool Recent { get; private set; }
    [TempData] public string? Notice { get; set; }
    public string AccountName { get; private set; } = "";
    public string Email { get; private set; } = "";
    public string? FailedId { get; private set; }
    public string? DraftName { get; private set; }
    public async Task OnGetAsync()
    {
        var user = await CurrentAsync();
        AccountName = user.Name; Email = user.Email!;
        Passkeys = await users.GetPasskeysAsync(user);
        Recent = await IsRecentAsync();
    }
    private async Task<AppUser> CurrentAsync() => await users.GetUserAsync(User) ?? throw new UnauthorizedAccessException();
    private async Task<bool> IsRecentAsync()
    {
        var session = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        return session.Properties?.IssuedUtc > DateTimeOffset.UtcNow.AddMinutes(-15);
    }
    public async Task<IActionResult> OnPostOptionsAsync()
    {
        if (!await IsRecentAsync()) return new JsonResult(new { error = text["RecentSignInRequired"].Value }) { StatusCode = 403 };
        var user = await CurrentAsync();
        if ((await users.GetPasskeysAsync(user)).Count >= 10) return new JsonResult(new { error = text["PasskeyLimit"].Value }) { StatusCode = 400 };
        return Content(await signIn.MakePasskeyCreationOptionsAsync(new PasskeyUserEntity { Id = user.Id, Name = user.Email!, DisplayName = user.Name }), "application/json");
    }
    public async Task<IActionResult> OnPostEnrollAsync(string credential, string name)
    {
        if (!await IsRecentAsync()) return new JsonResult(new { error = text["RecentSignInRequired"].Value }) { StatusCode = 403 };
        if (string.IsNullOrWhiteSpace(credential) || credential.Length > 100_000) return BadRequest();
        try
        {
            name = Rules.Text(name, 80);
            var user = await CurrentAsync();
            if ((await users.GetPasskeysAsync(user)).Count >= 10) throw new RuleException("PasskeyLimit");
            var result = await signIn.PerformPasskeyAttestationAsync(credential);
            if (!result.Succeeded)
            {
                logger.LogWarning("Passkey registration rejected: {Reason}. Request {RequestId}", result.Failure.Message, HttpContext.TraceIdentifier);
                throw new RuleException("PasskeyFailed");
            }
            if (result.UserEntity.Id != user.Id)
            {
                logger.LogWarning("Passkey registration account mismatch. Request {RequestId}", HttpContext.TraceIdentifier);
                throw new RuleException("PasskeyFailed");
            }
            result.Passkey.Name = name;
            var saved = await users.AddOrUpdatePasskeyAsync(user, result.Passkey);
            if (!saved.Succeeded)
            {
                logger.LogWarning("Passkey registration could not be stored: {ErrorCodes}. Request {RequestId}", string.Join(", ", saved.Errors.Select(error => error.Code)), HttpContext.TraceIdentifier);
                throw new RuleException("Conflict");
            }
            Notice = text["PasskeyAdded"];
            return new JsonResult(new { redirect = "/Account#passkey-" + Convert.ToHexString(result.Passkey.CredentialId) });
        }
        catch (Exception error) when (error is RuleException or System.Text.Json.JsonException or FormatException or InvalidOperationException)
        {
            if (error is not RuleException)
                logger.LogWarning("Passkey registration stopped with {ErrorType}: {Reason}. Request {RequestId}", error.GetType().Name, error.Message, HttpContext.TraceIdentifier);
            var key = error is RuleException ? error.Message : "PasskeyFailed";
            return new JsonResult(new { error = text[key == "Conflict" ? "AccountChangeConflict" : key].Value }) { StatusCode = 400 };
        }
    }
    public async Task<IActionResult> OnPostAsync(string action, string id, string? name)
    {
        if (!await IsRecentAsync()) return new JsonResult(new { error = text["RecentSignInRequired"].Value }) { StatusCode = 403 };
        if (string.IsNullOrWhiteSpace(id)) return BadRequest();
        try
        {
            var user = await CurrentAsync();
            var credentialId = WebEncoders.Base64UrlDecode(id);
            var passkey = await users.GetPasskeyAsync(user, credentialId) ?? throw new RuleException("NotFound");
            IdentityResult result;
            if (action == "remove") result = await users.RemovePasskeyAsync(user, credentialId);
            else if (action == "rename") { passkey.Name = Rules.Text(name, 80); result = await users.AddOrUpdatePasskeyAsync(user, passkey); }
            else throw new RuleException("InvalidValue");
            if (!result.Succeeded) throw new RuleException("Conflict");
            if (action == "remove") { await users.UpdateSecurityStampAsync(user); await signIn.RefreshSignInAsync(user); }
            Notice = text["Saved"];
            var url = "/Account#" + (action == "remove" ? "passkeys-title" : "passkey-" + Convert.ToHexString(credentialId));
            if (Request.Headers.ContainsKey("HX-Request")) { Response.Headers["HX-Redirect"] = url; return new EmptyResult(); }
            return Redirect(url);
        }
        catch (RuleException error)
        {
            Response.StatusCode = error.Message == "NotFound" ? 404 : error.Message == "Conflict" ? 409 : 422;
            var message = text[error.Message == "Conflict" ? "AccountChangeConflict" : error.Message].Value;
            if (Request.Headers.ContainsKey("HX-Request")) return new JsonResult(new { error = message });
            ModelState.AddModelError("", message); FailedId = id; DraftName = name;
        }
        catch (FormatException) { return BadRequest(); }
        await OnGetAsync(); return Page();
    }
}
