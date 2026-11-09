using Microsoft.EntityFrameworkCore;
using Steervo.Application.Evaluations;
using Steervo.Core;

namespace Steervo.Infrastructure.Data;

public sealed class EvaluationStore(AppDbContext db) : IEvaluationStore
{
    public async Task<IReadOnlyList<Evaluation>> ListVisibleAsync(string userId, CancellationToken cancellationToken = default) =>
        await db.Evaluations.AsNoTracking().Include(x => x.Shares).Include(x => x.Criteria)
            .Where(x => x.OwnerId == userId || x.Shares.Any(s => s.UserId == userId)).OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);
    public Task<Evaluation?> ReadAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Evaluations.AsNoTracking().Include(x => x.Groups).Include(x => x.Criteria).Include(x => x.Shares).AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    public async Task CreateAsync(Evaluation evaluation, CancellationToken cancellationToken = default)
    {
        db.Evaluations.Add(evaluation); await db.SaveChangesAsync(cancellationToken);
    }
    public async Task ChangeAsync(Guid id, long version, Action<Evaluation> change, bool delete = false, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var evaluation = await db.Evaluations.FromSqlInterpolated($"SELECT * FROM \"Evaluations\" WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken) ?? throw new RuleException("NotFound");
        await db.Entry(evaluation).Collection(x => x.Groups).LoadAsync(cancellationToken);
        await db.Entry(evaluation).Collection(x => x.Criteria).LoadAsync(cancellationToken);
        await db.Entry(evaluation).Collection(x => x.Shares).LoadAsync(cancellationToken);
        // Authorization is evaluated against the locked aggregate, including its current shares.
        change(evaluation);
        if (evaluation.Version != version) throw new RuleException("Conflict");
        evaluation.Version++;
        if (delete) db.Evaluations.Remove(evaluation);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new RuleException("Conflict"); }
        await transaction.CommitAsync(cancellationToken);
    }
}
