using Steervo.Core;

namespace Steervo.Application.Evaluations;

public interface ICurrentUser
{
    string Id { get; }
}

public interface IEvaluationStore
{
    Task<IReadOnlyList<Evaluation>> ListVisibleAsync(
        string userId,
        CancellationToken cancellationToken = default
    );

    Task<Evaluation?> ReadAsync(Guid id, CancellationToken cancellationToken = default);

    Task CreateAsync(Evaluation evaluation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a change to the stored evaluation if it still has the given version, and deletes
    /// it afterwards when requested. The change may throw to reject the operation.
    /// </summary>
    Task ChangeAsync(
        Guid id,
        long version,
        Action<Evaluation> change,
        bool delete = false,
        CancellationToken cancellationToken = default
    );
}

public interface IAccountDirectory
{
    Task<string?> FindConfirmedUserIdAsync(string email);

    Task<IReadOnlyDictionary<string, string>> EmailsAsync(IEnumerable<string> ids);
}

/// <summary>
/// Entry point for reading and changing evaluations on behalf of the current user. Every
/// operation checks the access level it needs.
/// </summary>
public sealed class EvaluationWorkspace(
    IEvaluationStore store,
    ICurrentUser current,
    IAccountDirectory accounts
)
{
    public Task<IReadOnlyList<Evaluation>> ListAsync() => store.ListVisibleAsync(current.Id);

    public async Task<Evaluation> ReadAsync(Guid id)
    {
        var evaluation = await store.ReadAsync(id) ?? throw new RuleException("NotFound");
        evaluation.Require(current.Id, AccessLevel.Reader);

        return evaluation;
    }

    public async Task<Guid> CreateAsync(
        string name,
        string? purpose,
        string? country,
        string? currency
    )
    {
        var evaluation = new Evaluation(current.Id, name, purpose);
        evaluation.SetProperties(name, purpose, country, currency);

        await store.CreateAsync(evaluation);

        return evaluation.Id;
    }

    public Task EditAsync(Guid id, long version, Action<Evaluation> change)
    {
        return store.ChangeAsync(
            id,
            version,
            evaluation =>
            {
                evaluation.Require(current.Id, AccessLevel.Editor);
                change(evaluation);
            }
        );
    }

    public Task DeleteAsync(Guid id, long version)
    {
        return store.ChangeAsync(
            id,
            version,
            evaluation => evaluation.Require(current.Id, AccessLevel.Owner),
            delete: true
        );
    }

    public async Task ShareAsync(Guid id, long version, string email, AccessLevel access)
    {
        // Check ownership before looking up another account.
        var evaluationToShare = await ReadAsync(id);
        evaluationToShare.Require(current.Id, AccessLevel.Owner);

        var userId =
            await accounts.FindConfirmedUserIdAsync(email)
            ?? throw new RuleException("AccountNotFound");

        await store.ChangeAsync(
            id,
            version,
            evaluation =>
            {
                evaluation.Require(current.Id, AccessLevel.Owner);
                evaluation.Grant(userId, access);
            }
        );
    }

    public Task RevokeAsync(Guid id, long version, string userId)
    {
        return store.ChangeAsync(
            id,
            version,
            evaluation =>
            {
                evaluation.Require(current.Id, AccessLevel.Owner);
                evaluation.Revoke(userId);
            }
        );
    }
}
