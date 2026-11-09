using Microsoft.EntityFrameworkCore;
using Steervo.Application.Evaluations;
using Steervo.Infrastructure.Data;

namespace Steervo.Infrastructure.Identity;

public sealed class AccountDirectory(AppDbContext db) : IAccountDirectory
{
    public Task<string?> FindConfirmedUserIdAsync(string email)
    {
        var normalized = (email ?? "").Trim().ToUpperInvariant();
        return db.Users.Where(x => x.NormalizedEmail == normalized && x.EmailConfirmed).Select(x => x.Id).SingleOrDefaultAsync();
    }
    public async Task<IReadOnlyDictionary<string, string>> NamesAsync(IEnumerable<string> ids) =>
        await db.Users.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Email!);
}
