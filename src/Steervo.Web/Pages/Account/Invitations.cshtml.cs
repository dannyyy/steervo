using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Steervo.Application.Accounts;
using Steervo.Application.Evaluations;
using Steervo.Core;

namespace Steervo.Web.Pages.Account;

/// <summary>Lets the primary account invite further people and revoke open invitations.</summary>
public sealed class InvitationsModel(
    IAccountAccess accounts,
    ICurrentUser current,
    IStringLocalizer<Ui> text
) : PageModel
{
    private const string HtmxRequestHeader = "HX-Request";
    private const string RevokeAction = "revoke";

    public IReadOnlyList<InvitationView> Invitations { get; private set; } = [];

    [BindProperty]
    public string Email { get; set; } = "";

    [BindProperty]
    public string Name { get; set; } = "";

    [TempData]
    public string? Notice { get; set; }

    /// <summary>True when sending an invitation was rejected, so that its editor opens again.</summary>
    public bool Failed { get; private set; }

    /// <summary>An invitation can still be used while it is neither accepted nor expired.</summary>
    public bool IsPending(InvitationView invitation)
    {
        return !invitation.Accepted && invitation.ExpiresAt > DateTimeOffset.UtcNow;
    }

    /// <summary>The CSS class that colours the status of an invitation.</summary>
    public string StatusClass(InvitationView invitation, bool pending)
    {
        if (invitation.Accepted)
        {
            return "accepted";
        }

        return pending ? "pending" : "expired";
    }

    /// <summary>The resource key of the status text of an invitation.</summary>
    public string StatusKey(InvitationView invitation, bool pending)
    {
        if (invitation.Accepted)
        {
            return "Accepted";
        }

        return pending ? "Pending" : "Expired";
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await accounts.IsPrimaryAsync(current.Id))
        {
            return StatusCode(403);
        }

        Invitations = await accounts.InvitationsAsync(current.Id);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? action, Guid id)
    {
        if (!await accounts.IsPrimaryAsync(current.Id))
        {
            return StatusCode(403);
        }

        var isRevocation = action == RevokeAction;

        try
        {
            if (isRevocation)
            {
                await accounts.RevokeInvitationAsync(current.Id, id);
            }
            else
            {
                await accounts.InviteAsync(current.Id, Email, Name);
            }

            Notice = text[isRevocation ? "InvitationRevoked" : "InvitationSent"];

            return RedirectAfterChange("/Account/Invitations#invitations-title");
        }
        catch (RuleException error)
        {
            Response.StatusCode = StatusCodeFor(error);

            if (Request.Headers.ContainsKey(HtmxRequestHeader))
            {
                return new JsonResult(new { error = text[error.Message].Value });
            }

            Failed = !isRevocation;
            ModelState.AddModelError("", text[error.Message]);
        }

        return await OnGetAsync();
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
            "Forbidden" => 403,
            _ => 422,
        };
    }
}
