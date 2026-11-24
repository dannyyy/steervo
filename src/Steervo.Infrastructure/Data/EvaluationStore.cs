using System.Data;
using Microsoft.EntityFrameworkCore;
using Steervo.Application.Evaluations;
using Steervo.Core;
using Steervo.Infrastructure.Documents;

namespace Steervo.Infrastructure.Data;

/// <summary>
/// Loads and saves evaluations as whole aggregates. Changes run under a row lock and an
/// optimistic version check, so concurrent edits end in a conflict instead of a lost update.
/// </summary>
public sealed class EvaluationStore(AppDbContext db, DocumentCleanup? cleanup = null)
    : IEvaluationStore
{
    private const string NotFound = "NotFound";
    private const string Conflict = "Conflict";

    public async Task<IReadOnlyList<Evaluation>> ListVisibleAsync(
        string userId,
        CancellationToken cancellationToken = default
    )
    {
        return await db
            .Evaluations.AsNoTracking()
            .Include(evaluation => evaluation.Shares)
            .Include(evaluation => evaluation.Criteria)
            .Include(evaluation => evaluation.Candidates)
            .AsSplitQuery()
            .Where(evaluation =>
                evaluation.OwnerId == userId
                || evaluation.Shares.Any(share => share.UserId == userId)
            )
            .OrderByDescending(evaluation => evaluation.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<Evaluation?> ReadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Split queries must observe one consistent set of offer and document versions.
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken
        );

        var evaluation = await db
            .Evaluations.AsNoTracking()
            .Include(root => root.Groups)
            .Include(root => root.Criteria)
            .Include(root => root.Shares)
            .Include(root => root.Candidates)
            .Include(root => root.Assessments)
            .Include(root => root.Offers)
                .ThenInclude(offer => offer.Revisions)
                    .ThenInclude(revision => revision.Lines)
            .Include(root => root.OfferAssessments)
            .Include(root => root.Documents)
                .ThenInclude(document => document.Revisions)
            .Include(root => root.CostPlanning)
            .Include(root => root.Costs)
            .Include(root => root.CalculationSettings)
            .Include(root => root.Scenarios)
                .ThenInclude(scenario => scenario.Residuals)
            .Include(root => root.Scenarios)
                .ThenInclude(scenario => scenario.CostAdjustments)
            .AsSplitQuery()
            .SingleOrDefaultAsync(root => root.Id == id, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return evaluation;
    }

    public async Task CreateAsync(
        Evaluation evaluation,
        CancellationToken cancellationToken = default
    )
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        db.Evaluations.Add(evaluation);
        await AttachFilesAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ChangeAsync(
        Guid id,
        long version,
        Action<Evaluation> change,
        bool delete = false,
        CancellationToken cancellationToken = default
    )
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var evaluation = await LockAsync(id, cancellationToken);
        await LoadAggregateAsync(evaluation, cancellationToken);

        // Authorization is evaluated against the locked aggregate, including its current shares.
        change(evaluation);

        if (evaluation.Version != version)
        {
            throw new RuleException(Conflict);
        }

        evaluation.Version++;

        if (delete)
        {
            db.Evaluations.Remove(evaluation);
        }

        await AttachFilesAsync(cancellationToken);
        await SaveAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        // Cleanup opens transactions of its own, so this one has to be gone first.
        await transaction.DisposeAsync();

        if (cleanup is not null)
        {
            await cleanup.RunAsync(cancellationToken: cancellationToken);
        }
    }

    private async Task<Evaluation> LockAsync(Guid id, CancellationToken cancellationToken)
    {
        var evaluation = await db
            .Evaluations.FromSqlInterpolated(
                $"SELECT * FROM \"Evaluations\" WHERE \"Id\" = {id} FOR UPDATE"
            )
            .SingleOrDefaultAsync(cancellationToken);

        return evaluation ?? throw new RuleException(NotFound);
    }

    // Loads every part of the aggregate after the row lock is held, so nothing read here can
    // change before the transaction commits.
    private async Task LoadAggregateAsync(
        Evaluation evaluation,
        CancellationToken cancellationToken
    )
    {
        var entry = db.Entry(evaluation);

        await entry.Collection(loaded => loaded.Groups).LoadAsync(cancellationToken);
        await entry.Collection(loaded => loaded.Criteria).LoadAsync(cancellationToken);
        await entry.Collection(loaded => loaded.Shares).LoadAsync(cancellationToken);
        await entry.Collection(loaded => loaded.Candidates).LoadAsync(cancellationToken);
        await entry.Collection(loaded => loaded.Assessments).LoadAsync(cancellationToken);

        await entry
            .Collection(loaded => loaded.Offers)
            .Query()
            .Include(offer => offer.Revisions)
                .ThenInclude(revision => revision.Lines)
            .AsSplitQuery()
            .LoadAsync(cancellationToken);
        await entry.Collection(loaded => loaded.OfferAssessments).LoadAsync(cancellationToken);

        await entry
            .Collection(loaded => loaded.Documents)
            .Query()
            .Include(document => document.Revisions)
            .AsSplitQuery()
            .LoadAsync(cancellationToken);

        await entry.Reference(loaded => loaded.CostPlanning).LoadAsync(cancellationToken);
        await entry.Collection(loaded => loaded.Costs).LoadAsync(cancellationToken);
        await entry.Reference(loaded => loaded.CalculationSettings).LoadAsync(cancellationToken);

        await entry
            .Collection(loaded => loaded.Scenarios)
            .Query()
            .Include(scenario => scenario.Residuals)
            .Include(scenario => scenario.CostAdjustments)
            .AsSplitQuery()
            .LoadAsync(cancellationToken);
    }

    // A new document revision takes ownership of its uploaded file by removing the pending
    // cleanup entry. An upload whose entry is already due may have been deleted and is rejected.
    private async Task AttachFilesAsync(CancellationToken cancellationToken)
    {
        var addedRevisions = db
            .ChangeTracker.Entries<DocumentRevision>()
            .Where(tracked => tracked.State == EntityState.Added);

        foreach (var entry in addedRevisions)
        {
            var id = entry.Entity.File.ContentId;
            var claimed = await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM \"PendingDocumentDeletion\" WHERE \"Id\" = {id} AND \"AvailableAt\" > CURRENT_TIMESTAMP",
                cancellationToken
            );

            if (claimed != 1)
            {
                throw new RuleException("DocumentUploadExpired");
            }
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new RuleException(Conflict);
        }
    }
}
