using Microsoft.EntityFrameworkCore;
using Steervo.Application.Evaluations;
using Steervo.Core;

namespace Steervo.Infrastructure.Data;

public sealed class EvaluationStore(AppDbContext db, Steervo.Infrastructure.Documents.DocumentCleanup? cleanup = null) : IEvaluationStore
{
    public async Task<IReadOnlyList<Evaluation>> ListVisibleAsync(string userId, CancellationToken cancellationToken = default) =>
        await db.Evaluations.AsNoTracking().Include(x => x.Shares).Include(x => x.Criteria).Include(x => x.Candidates).AsSplitQuery()
            .Where(x => x.OwnerId == userId || x.Shares.Any(s => s.UserId == userId)).OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);
    public async Task<Evaluation?> ReadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Split queries must observe one consistent set of offer and document versions.
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, cancellationToken);
        var evaluation = await db.Evaluations.AsNoTracking().Include(x => x.Groups).Include(x => x.Criteria).Include(x => x.Shares).Include(x => x.Candidates).Include(x => x.Assessments).Include(x => x.Offers).ThenInclude(x => x.Revisions).ThenInclude(x => x.Lines).Include(x => x.OfferAssessments).Include(x => x.Documents).ThenInclude(x => x.Revisions).Include(x => x.CostPlanning).Include(x => x.Costs).Include(x => x.CalculationSettings).Include(x => x.Scenarios).ThenInclude(x => x.Residuals).Include(x => x.Scenarios).ThenInclude(x => x.CostAdjustments).AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return evaluation;
    }
    public async Task CreateAsync(Evaluation evaluation, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Evaluations.Add(evaluation);
        await AttachFilesAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
    public async Task ChangeAsync(Guid id, long version, Action<Evaluation> change, bool delete = false, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var evaluation = await db.Evaluations.FromSqlInterpolated($"SELECT * FROM \"Evaluations\" WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken) ?? throw new RuleException("NotFound");
        await db.Entry(evaluation).Collection(x => x.Groups).LoadAsync(cancellationToken);
        await db.Entry(evaluation).Collection(x => x.Criteria).LoadAsync(cancellationToken);
        await db.Entry(evaluation).Collection(x => x.Shares).LoadAsync(cancellationToken);
        await db.Entry(evaluation).Collection(x => x.Candidates).LoadAsync(cancellationToken);
        await db.Entry(evaluation).Collection(x => x.Assessments).LoadAsync(cancellationToken);
        await db.Entry(evaluation).Collection(x => x.Offers).Query().Include(x => x.Revisions).ThenInclude(x => x.Lines).AsSplitQuery().LoadAsync(cancellationToken);
        await db.Entry(evaluation).Collection(x => x.OfferAssessments).LoadAsync(cancellationToken);
        await db.Entry(evaluation).Collection(x => x.Documents).Query().Include(x => x.Revisions).AsSplitQuery().LoadAsync(cancellationToken);
        await db.Entry(evaluation).Reference(x => x.CostPlanning).LoadAsync(cancellationToken);
        await db.Entry(evaluation).Collection(x => x.Costs).LoadAsync(cancellationToken);
        await db.Entry(evaluation).Reference(x => x.CalculationSettings).LoadAsync(cancellationToken);
        await db.Entry(evaluation).Collection(x => x.Scenarios).Query().Include(x => x.Residuals).Include(x => x.CostAdjustments).AsSplitQuery().LoadAsync(cancellationToken);
        // Authorization is evaluated against the locked aggregate, including its current shares.
        change(evaluation);
        if (evaluation.Version != version) throw new RuleException("Conflict");
        evaluation.Version++;
        if (delete) db.Evaluations.Remove(evaluation);
        await AttachFilesAsync(cancellationToken);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new RuleException("Conflict"); }
        await transaction.CommitAsync(cancellationToken);
        await transaction.DisposeAsync();
        if (cleanup is not null) await cleanup.RunAsync(cancellationToken: cancellationToken);
    }
    private async Task AttachFilesAsync(CancellationToken cancellationToken)
    {
        foreach (var entry in db.ChangeTracker.Entries<DocumentRevision>().Where(x => x.State == EntityState.Added))
        {
            var id = entry.Entity.File.ContentId;
            var claimed = await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"PendingDocumentDeletion\" WHERE \"Id\" = {id} AND \"AvailableAt\" > CURRENT_TIMESTAMP", cancellationToken);
            if (claimed != 1) throw new RuleException("DocumentUploadExpired");
        }
    }
}
