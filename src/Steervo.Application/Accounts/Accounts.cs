namespace Steervo.Application.Accounts;

public sealed record InvitationView(Guid Id, string Email, DateTimeOffset ExpiresAt, bool Accepted);
public interface IAccountAccess
{
    Task<bool> IsInitializedAsync();
    Task<bool> IsPrimaryAsync(string userId);
    Task<Guid> BeginSetupAsync(string setupToken, string email, string name);
    Task<Guid> RequestCodeAsync(string email);
    Task<string> VerifyCodeAsync(Guid id, string code);
    Task InviteAsync(string actorId, string email, string name);
    Task<IReadOnlyList<InvitationView>> InvitationsAsync(string actorId);
    Task RevokeInvitationAsync(string actorId, Guid id);
}

public interface IAccountMail
{
    Task SendCodeAsync(string email, string code);
    Task SendInvitationAsync(string email);
}
