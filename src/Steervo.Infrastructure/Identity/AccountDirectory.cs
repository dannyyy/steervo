using Microsoft.EntityFrameworkCore;
using Steervo.Application.Evaluations;
using Steervo.Infrastructure.Data;

namespace Steervo.Infrastructure.Identity;

/// <summary>
/// Looks up accounts for sharing: by e-mail address and by id.
/// </summary>
public sealed class AccountDirectory(AppDbContext db) : IAccountDirectory
{
    public Task<string?> FindConfirmedUserIdAsync(string email)
    {
        var normalized = (email ?? "").Trim().ToUpperInvariant();

        return db
            .Users.Where(user => user.NormalizedEmail == normalized && user.EmailConfirmed)
            .Select(user => user.Id)
            .SingleOrDefaultAsync();
    }

    // Accounts are presented to other users by their e-mail address.
    public async Task<IReadOnlyDictionary<string, string>> EmailsAsync(IEnumerable<string> ids)
    {
        return await db
            .Users.Where(user => ids.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.Email!);
    }
}
