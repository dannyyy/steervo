using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Steervo.Application.Accounts;
using Steervo.Core;
using Steervo.Infrastructure.Data;

namespace Steervo.Infrastructure.Identity;

public sealed class SetupOptions { public string Token { get; set; } = ""; }

public sealed class AccountAccess(AppDbContext db, UserManager<AppUser> users, IDataProtectionProvider protection,
    IAccountMail mail, IOptions<SetupOptions> setup, TimeProvider clock) : IAccountAccess
{
    private readonly IDataProtector protector = protection.CreateProtector("Steervo.EmailCode.v1");
    public Task<bool> IsInitializedAsync() => db.Installations.AnyAsync();
    public Task<bool> IsPrimaryAsync(string userId) => db.Installations.AnyAsync(x => x.PrimaryUserId == userId);
    public async Task<Guid> BeginSetupAsync(string setupToken, string email, string name)
    {
        if (await IsInitializedAsync()) throw new RuleException("SetupClosed");
        if (setup.Value.Token.Length < 32 || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(setupToken ?? "")), SHA256.HashData(Encoding.UTF8.GetBytes(setup.Value.Token)))) throw new RuleException("InvalidSetupToken");
        return await IssueAsync(Normalize(email), Rules.Text(name), true);
    }
    public Task<Guid> RequestCodeAsync(string email) => IssueAsync(Normalize(email), "", false);
    private async Task<Guid> IssueAsync(string email, string name, bool isSetup)
    {
        var now = clock.GetUtcNow();
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var challenge = new EmailChallenge { Email = email, Name = name, IsSetup = isSetup, CreatedAt = now, ExpiresAt = now.AddMinutes(10), ProtectedCode = protector.Protect(code) };
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await LockAccountsAsync();
            if (isSetup && await IsInitializedAsync()) throw new RuleException("SetupClosed");
            await db.Challenges.Where(x => x.CreatedAt < now.AddDays(-1)).ExecuteDeleteAsync();
            var recent = await db.Challenges.Where(x => x.Email == email && x.CreatedAt > now.AddHours(-1)).ToListAsync();
            if (recent.Count >= 5 || recent.Any(x => x.CreatedAt > now.AddSeconds(-60))) throw new RuleException("CodeRateLimit");
            var eligible = isSetup || await db.Users.AnyAsync(x => x.NormalizedEmail == email) || await db.Invitations.AnyAsync(x => x.Email == email && x.AcceptedAt == null && x.ExpiresAt > now);
            // Ineligible addresses receive the same browser flow, without creating an account or sending mail.
            if (!eligible) return Guid.NewGuid();
            foreach (var old in recent.Where(x => x.ConsumedAt == null)) old.ConsumedAt = now;
            db.Challenges.Add(challenge);
            await db.SaveChangesAsync(); await transaction.CommitAsync();
        }
        try
        {
            await mail.SendCodeAsync(email, code);
            challenge.Delivered = true; await db.SaveChangesAsync();
        }
        catch
        {
            challenge.ConsumedAt = now; await db.SaveChangesAsync(); throw;
        }
        return challenge.Id;
    }
    public async Task<string> VerifyCodeAsync(Guid id, string code)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await LockAccountsAsync();
        var now = clock.GetUtcNow();
        var challenge = await db.Challenges.SingleOrDefaultAsync(x => x.Id == id);
        if (challenge is null || !challenge.Delivered || challenge.ConsumedAt != null || challenge.ExpiresAt <= now || challenge.Attempts >= 5) throw new RuleException("InvalidCode");
        challenge.Attempts++;
        code ??= "";
        var valid = code.Length == 6 && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(protector.Unprotect(challenge.ProtectedCode)), Encoding.UTF8.GetBytes(code));
        if (!valid)
        {
            await db.SaveChangesAsync(); await transaction.CommitAsync(); throw new RuleException("InvalidCode");
        }
        var user = await users.FindByEmailAsync(challenge.Email);
        if (challenge.IsSetup)
        {
            if (await IsInitializedAsync()) throw new RuleException("SetupClosed");
            user = await CreateUserAsync(challenge.Email, challenge.Name);
            db.Installations.Add(new Installation { PrimaryUserId = user.Id });
        }
        else if (user is null)
        {
            var invitation = await db.Invitations.SingleOrDefaultAsync(x => x.Email == challenge.Email && x.AcceptedAt == null && x.ExpiresAt > now)
                ?? throw new RuleException("InvitationExpired");
            user = await CreateUserAsync(challenge.Email, invitation.Name);
            invitation.AcceptedAt = now;
        }
        challenge.ConsumedAt = now;
        await db.SaveChangesAsync(); await transaction.CommitAsync();
        return user.Id;
    }
    private async Task<AppUser> CreateUserAsync(string email, string name)
    {
        var user = new AppUser { UserName = email, Email = email, NormalizedEmail = email, EmailConfirmed = true, Name = name };
        var result = await users.CreateAsync(user);
        if (!result.Succeeded) throw new RuleException("InvalidAccount");
        return user;
    }
    public async Task InviteAsync(string actorId, string email, string name)
    {
        var normalized = Normalize(email); name = Rules.Text(name);
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await LockAccountsAsync(); await RequirePrimaryAsync(actorId);
            if (await db.Users.AnyAsync(x => x.NormalizedEmail == normalized)) throw new RuleException("AccountExists");
            var invitation = await db.Invitations.SingleOrDefaultAsync(x => x.Email == normalized);
            if (invitation is null) { invitation = new Invitation { Email = normalized }; db.Invitations.Add(invitation); }
            invitation.Name = name; invitation.ExpiresAt = clock.GetUtcNow().AddDays(7); invitation.AcceptedAt = null;
            await db.SaveChangesAsync(); await transaction.CommitAsync();
        }
        await mail.SendInvitationAsync(normalized);
    }
    public async Task<IReadOnlyList<InvitationView>> InvitationsAsync(string actorId)
    {
        await RequirePrimaryAsync(actorId);
        return await db.Invitations.OrderByDescending(x => x.ExpiresAt).Select(x => new InvitationView(x.Id, x.Email, x.ExpiresAt, x.AcceptedAt != null)).ToListAsync();
    }
    public async Task RevokeInvitationAsync(string actorId, Guid id)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await LockAccountsAsync(); await RequirePrimaryAsync(actorId);
        var invitation = await db.Invitations.SingleOrDefaultAsync(x => x.Id == id && x.AcceptedAt == null) ?? throw new RuleException("NotFound");
        invitation.ExpiresAt = clock.GetUtcNow(); await db.SaveChangesAsync(); await transaction.CommitAsync();
    }
    private async Task RequirePrimaryAsync(string actorId) { if (!await IsPrimaryAsync(actorId)) throw new RuleException("Forbidden"); }
    private Task LockAccountsAsync() => db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(78231401)");
    private static string Normalize(string email)
    {
        var value = (email ?? "").Trim();
        if (value.Length > 254 || !MailAddress.TryCreate(value, out var parsed) || parsed.Address != value) throw new RuleException("InvalidEmail");
        return value.ToUpperInvariant();
    }
}
