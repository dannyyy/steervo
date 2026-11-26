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

/// <summary>
/// The secret an operator must present to create the first account of an installation.
/// </summary>
public sealed class SetupOptions
{
    public string Token { get; set; } = "";
}

/// <summary>
/// Sign-in by e-mail code, first-time setup and invitations. Every state change runs under one
/// database-wide lock, so limits and one-time rules cannot be bypassed by parallel requests.
/// </summary>
public sealed class AccountAccess(
    AppDbContext db,
    UserManager<AppUser> users,
    IDataProtectionProvider protection,
    IAccountMail mail,
    IOptions<SetupOptions> setup,
    TimeProvider clock
) : IAccountAccess
{
    private const int MinimumSetupTokenLength = 32;
    private const int MaximumEmailLength = 254;

    // Codes are six decimal digits; the upper bound and the format below belong together.
    private const int CodeLength = 6;
    private const int CodeUpperBound = 1_000_000;
    private const string CodeFormat = "D6";
    private const int CodeLifetimeMinutes = 10;
    private const int MaximumCodesPerHour = 5;
    private const int ResendWaitSeconds = 60;
    private const int MaximumAttempts = 5;
    private const int InvitationLifetimeDays = 7;

    private readonly IDataProtector protector = protection.CreateProtector("Steervo.EmailCode.v1");

    public Task<bool> IsInitializedAsync() => db.Installations.AnyAsync();

    public Task<bool> IsPrimaryAsync(string userId)
    {
        return db.Installations.AnyAsync(installation => installation.PrimaryUserId == userId);
    }

    public async Task<Guid> BeginSetupAsync(string setupToken, string email, string name)
    {
        if (await IsInitializedAsync())
        {
            throw new RuleException("SetupClosed");
        }

        if (!IsSetupToken(setupToken))
        {
            throw new RuleException("InvalidSetupToken");
        }

        return await IssueAsync(Normalize(email), Rules.Text(name), true);
    }

    public Task<Guid> RequestCodeAsync(string email) => IssueAsync(Normalize(email), "", false);

    public async Task<string> VerifyCodeAsync(Guid id, string code)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await LockAccountsAsync();

        var now = clock.GetUtcNow();
        var challenge = await db.Challenges.SingleOrDefaultAsync(stored => stored.Id == id);

        if (challenge is null || !IsOpen(challenge, now))
        {
            throw new RuleException("InvalidCode");
        }

        // The attempt is counted before the comparison and stays counted when the code is wrong.
        challenge.Attempts++;

        if (!MatchesCode(challenge, code))
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            throw new RuleException("InvalidCode");
        }

        var user = await ResolveUserAsync(challenge, now);

        challenge.ConsumedAt = now;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        return user.Id;
    }

    public async Task InviteAsync(string actorId, string email, string name)
    {
        var normalized = Normalize(email);
        name = Rules.Text(name);

        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await LockAccountsAsync();
            await RequirePrimaryAsync(actorId);

            if (await db.Users.AnyAsync(user => user.NormalizedEmail == normalized))
            {
                throw new RuleException("AccountExists");
            }

            // Inviting an address again renews its existing invitation.
            var invitation = await db.Invitations.SingleOrDefaultAsync(existing =>
                existing.Email == normalized
            );

            if (invitation is null)
            {
                invitation = new Invitation { Email = normalized };
                db.Invitations.Add(invitation);
            }

            invitation.Name = name;
            invitation.ExpiresAt = clock.GetUtcNow().AddDays(InvitationLifetimeDays);
            invitation.AcceptedAt = null;

            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await mail.SendInvitationAsync(normalized);
    }

    public async Task<IReadOnlyList<InvitationView>> InvitationsAsync(string actorId)
    {
        await RequirePrimaryAsync(actorId);

        return await db
            .Invitations.OrderByDescending(invitation => invitation.ExpiresAt)
            .Select(invitation => new InvitationView(
                invitation.Id,
                invitation.Email,
                invitation.ExpiresAt,
                invitation.AcceptedAt != null
            ))
            .ToListAsync();
    }

    public async Task RevokeInvitationAsync(string actorId, Guid id)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await LockAccountsAsync();
        await RequirePrimaryAsync(actorId);

        var invitation = await db.Invitations.SingleOrDefaultAsync(pending =>
            pending.Id == id && pending.AcceptedAt == null
        ) ?? throw new RuleException("NotFound");

        // Revoking keeps the row and lets it expire at once.
        invitation.ExpiresAt = clock.GetUtcNow();

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    // Both sides are hashed first, so the comparison takes the same time for any input length.
    private bool IsSetupToken(string setupToken)
    {
        var expected = setup.Value.Token;

        if (expected.Length < MinimumSetupTokenLength)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(setupToken ?? "")),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected))
        );
    }

    // Stores a new code for the address and mails it. The returned challenge id is what the
    // browser presents again together with the code.
    private async Task<Guid> IssueAsync(string email, string name, bool isSetup)
    {
        var now = clock.GetUtcNow();
        var code = RandomNumberGenerator.GetInt32(0, CodeUpperBound).ToString(CodeFormat);
        var challenge = new EmailChallenge
        {
            Email = email,
            Name = name,
            IsSetup = isSetup,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(CodeLifetimeMinutes),
            ProtectedCode = protector.Protect(code),
        };

        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await LockAccountsAsync();

            if (isSetup && await IsInitializedAsync())
            {
                throw new RuleException("SetupClosed");
            }

            await db
                .Challenges.Where(expired => expired.CreatedAt < now.AddDays(-1))
                .ExecuteDeleteAsync();

            var recent = await db
                .Challenges.Where(issued =>
                    issued.Email == email && issued.CreatedAt > now.AddHours(-1)
                )
                .ToListAsync();

            if (IsRateLimited(recent, now))
            {
                throw new RuleException("CodeRateLimit");
            }

            // Ineligible addresses receive the same browser flow, without creating an account or sending mail.
            if (!await IsEligibleAsync(email, isSetup, now))
            {
                return Guid.NewGuid();
            }

            // Only the newest code of an address stays usable.
            foreach (var earlier in recent.Where(issued => issued.ConsumedAt == null))
            {
                earlier.ConsumedAt = now;
            }

            db.Challenges.Add(challenge);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await DeliverAsync(challenge, code, now);

        return challenge.Id;
    }

    private static bool IsRateLimited(List<EmailChallenge> recent, DateTimeOffset now)
    {
        return recent.Count >= MaximumCodesPerHour
            || recent.Any(issued => issued.CreatedAt > now.AddSeconds(-ResendWaitSeconds));
    }

    // A code is sent for the setup itself, to an existing account, or to an invited address.
    private async Task<bool> IsEligibleAsync(string email, bool isSetup, DateTimeOffset now)
    {
        return isSetup
            || await db.Users.AnyAsync(user => user.NormalizedEmail == email)
            || await db.Invitations.AnyAsync(invitation =>
                invitation.Email == email
                && invitation.AcceptedAt == null
                && invitation.ExpiresAt > now
            );
    }

    // A challenge can only be verified once its code was handed to the mail server. One that
    // could not be delivered is closed immediately.
    private async Task DeliverAsync(EmailChallenge challenge, string code, DateTimeOffset now)
    {
        try
        {
            await mail.SendCodeAsync(challenge.Email, code);
            challenge.Delivered = true;
            await db.SaveChangesAsync();
        }
        catch
        {
            challenge.ConsumedAt = now;
            await db.SaveChangesAsync();

            throw;
        }
    }

    private static bool IsOpen(EmailChallenge challenge, DateTimeOffset now)
    {
        return challenge.Delivered
            && challenge.ConsumedAt == null
            && challenge.ExpiresAt > now
            && challenge.Attempts < MaximumAttempts;
    }

    private bool MatchesCode(EmailChallenge challenge, string code)
    {
        code ??= "";

        return code.Length == CodeLength
            && CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(protector.Unprotect(challenge.ProtectedCode)),
                Encoding.UTF8.GetBytes(code)
            );
    }

    // Returns the account the verified challenge signs in, creating it for the setup and for a
    // first sign-in by invitation.
    private async Task<AppUser> ResolveUserAsync(EmailChallenge challenge, DateTimeOffset now)
    {
        var user = await users.FindByEmailAsync(challenge.Email);

        if (challenge.IsSetup)
        {
            if (await IsInitializedAsync())
            {
                throw new RuleException("SetupClosed");
            }

            user = await CreateUserAsync(challenge.Email, challenge.Name);
            db.Installations.Add(new Installation { PrimaryUserId = user.Id });
        }
        else if (user is null)
        {
            var invitation = await db.Invitations.SingleOrDefaultAsync(pending =>
                pending.Email == challenge.Email
                && pending.AcceptedAt == null
                && pending.ExpiresAt > now
            ) ?? throw new RuleException("InvitationExpired");

            user = await CreateUserAsync(challenge.Email, invitation.Name);
            invitation.AcceptedAt = now;
        }

        return user;
    }

    private async Task<AppUser> CreateUserAsync(string email, string name)
    {
        var user = new AppUser
        {
            UserName = email,
            Email = email,
            NormalizedEmail = email,
            EmailConfirmed = true,
            Name = name,
        };
        var result = await users.CreateAsync(user);

        if (!result.Succeeded)
        {
            throw new RuleException("InvalidAccount");
        }

        return user;
    }

    private async Task RequirePrimaryAsync(string actorId)
    {
        if (!await IsPrimaryAsync(actorId))
        {
            throw new RuleException("Forbidden");
        }
    }

    // Serialises all account changes within the current transaction.
    private Task LockAccountsAsync()
    {
        return db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(78231401)");
    }

    // Accepts a plain address only (no display name) and returns its upper-case lookup form.
    private static string Normalize(string email)
    {
        var value = (email ?? "").Trim();
        var isPlainAddress =
            value.Length <= MaximumEmailLength
            && MailAddress.TryCreate(value, out var parsed)
            && parsed.Address == value;

        if (!isPlainAddress)
        {
            throw new RuleException("InvalidEmail");
        }

        return value.ToUpperInvariant();
    }
}
