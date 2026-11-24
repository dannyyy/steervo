using System.Security.Claims;
using Microsoft.Extensions.Localization;
using Steervo.Application.Accounts;
using Steervo.Application.Evaluations;
using Steervo.Infrastructure.Mail;

namespace Steervo.Web;

/// <summary>Marker type that names the shared resource file with all interface texts.</summary>
public sealed class Ui;

/// <summary>Resolves the signed-in user of the current request.</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string Id =>
        accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException();
}

/// <summary>Sends the account mails with their localized subject and body.</summary>
public sealed class LocalizedAccountMail(
    SmtpDelivery delivery,
    IStringLocalizer<Ui> text,
    IConfiguration configuration
) : IAccountMail
{
    private const string DefaultPublicUrl = "http://localhost:5180";

    public Task SendCodeAsync(string email, string code)
    {
        return delivery.SendAsync(email, text["CodeSubject"], text["CodeBody", code]);
    }

    public Task SendInvitationAsync(string email)
    {
        var publicUrl = configuration["PublicUrl"] ?? DefaultPublicUrl;

        return delivery.SendAsync(
            email,
            text["InvitationSubject"],
            text["InvitationBody", publicUrl]
        );
    }
}
