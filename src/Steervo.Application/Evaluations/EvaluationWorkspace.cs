using Steervo.Core;

namespace Steervo.Application.Evaluations;

public interface ICurrentUser { string Id { get; } }

public interface IEvaluationStore
{
    Task<IReadOnlyList<Evaluation>> ListVisibleAsync(string userId, CancellationToken cancellationToken = default);
    Task<Evaluation?> ReadAsync(Guid id, CancellationToken cancellationToken = default);
    Task CreateAsync(Evaluation evaluation, CancellationToken cancellationToken = default);
    Task ChangeAsync(Guid id, long version, Action<Evaluation> change, bool delete = false, CancellationToken cancellationToken = default);
}

public interface IAccountDirectory
{
    Task<string?> FindConfirmedUserIdAsync(string email);
    Task<IReadOnlyDictionary<string, string>> NamesAsync(IEnumerable<string> ids);
}

public sealed class EvaluationWorkspace(IEvaluationStore store, ICurrentUser current, IAccountDirectory accounts)
{
    public Task<IReadOnlyList<Evaluation>> ListAsync() => store.ListVisibleAsync(current.Id);
    public async Task<Evaluation> ReadAsync(Guid id)
    {
        var evaluation = await store.ReadAsync(id) ?? throw new RuleException("NotFound");
        evaluation.Require(current.Id, AccessLevel.Reader);
        return evaluation;
    }
    public async Task<Guid> CreateAsync(string name, string? purpose)
    {
        var evaluation = new Evaluation(current.Id, name, purpose);
        await store.CreateAsync(evaluation);
        return evaluation.Id;
    }
    public Task EditAsync(Guid id, long version, Action<Evaluation> change) =>
        store.ChangeAsync(id, version, evaluation => { evaluation.Require(current.Id, AccessLevel.Editor); change(evaluation); });
    public Task DeleteAsync(Guid id, long version) =>
        store.ChangeAsync(id, version, evaluation => evaluation.Require(current.Id, AccessLevel.Owner), delete: true);
    public async Task ShareAsync(Guid id, long version, string email, AccessLevel access)
    {
        // Check ownership before looking up another account.
        (await ReadAsync(id)).Require(current.Id, AccessLevel.Owner);
        var userId = await accounts.FindConfirmedUserIdAsync(email) ?? throw new RuleException("AccountNotFound");
        await store.ChangeAsync(id, version, evaluation => { evaluation.Require(current.Id, AccessLevel.Owner); evaluation.Grant(userId, access); });
    }
    public Task RevokeAsync(Guid id, long version, string userId) =>
        store.ChangeAsync(id, version, evaluation => { evaluation.Require(current.Id, AccessLevel.Owner); evaluation.Revoke(userId); });
}
