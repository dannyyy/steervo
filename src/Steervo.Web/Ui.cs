using System.Security.Claims;
using Microsoft.Extensions.Localization;
using Steervo.Application.Accounts;
using Steervo.Application.Evaluations;
using Steervo.Infrastructure.Mail;

namespace Steervo.Web;

public sealed class Ui;
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string Id => accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();
}
public sealed class LocalizedAccountMail(SmtpDelivery delivery, IStringLocalizer<Ui> text, IConfiguration configuration) : IAccountMail
{
    public Task SendCodeAsync(string email, string code) => delivery.SendAsync(email, text["CodeSubject"], text["CodeBody", code]);
    public Task SendInvitationAsync(string email) => delivery.SendAsync(email, text["InvitationSubject"], text["InvitationBody", configuration["PublicUrl"] ?? "http://localhost:5180"]);
}
