namespace Steervo.Core;

public enum AccessLevel { Reader, Editor, Owner }
public enum CriterionType { Boolean, Measurement, Rating }
public enum Priority { Required, Preferred, Optional }
public enum CriterionScope { Vehicle, Offer }
public enum Comparison { AtLeast, AtMost }
public enum Fulfillment { Unknown, Met, Unmet, Rated }

public sealed class RuleException(string code) : Exception(code);

public static class Rules
{
    public static string Text(string? value, int max = 160)
    {
        var result = value?.Trim() ?? "";
        if (result.Length is 0 || result.Length > max) throw new RuleException("InvalidText");
        return result;
    }
    public static T Defined<T>(T value) where T : struct, Enum =>
        Enum.IsDefined(value) ? value : throw new RuleException("InvalidValue");
}

public sealed class Evaluation
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string OwnerId { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string Purpose { get; private set; } = "";
    public long Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public List<CriterionGroup> Groups { get; private set; } = [];
    public List<Criterion> Criteria { get; private set; } = [];
    public List<CaseShare> Shares { get; private set; } = [];
    private Evaluation() { }
    public Evaluation(string ownerId, string name, string? purpose)
    {
        OwnerId = Rules.Text(ownerId);
        Rename(name, purpose);
    }
    public AccessLevel? AccessFor(string userId) => OwnerId == userId ? AccessLevel.Owner : Shares.SingleOrDefault(x => x.UserId == userId)?.Access;
    public void Require(string userId, AccessLevel minimum)
    {
        if (AccessFor(userId) is not { } actual || actual < minimum) throw new RuleException("Forbidden");
    }
    public void Rename(string name, string? purpose)
    {
        Name = Rules.Text(name);
        Purpose = purpose?.Trim() ?? "";
        if (Purpose.Length > 2000) throw new RuleException("InvalidText");
    }
    public Guid AddGroup(string name)
    {
        var group = new CriterionGroup(Id, Rules.Text(name), Groups.Count);
        Groups.Add(group);
        return group.Id;
    }
    public void RenameGroup(Guid id, string name) => Group(id).Name = Rules.Text(name);
    public void MoveGroup(Guid id, int direction) => Move(Groups.OrderBy(x => x.Position).ToList(), Group(id), direction, (x, i) => x.Position = i);
    public void PlaceGroup(Guid id, int position) => Place(Groups.OrderBy(x => x.Position).ToList(), Group(id), position, (x, i) => x.Position = i);
    public void RemoveGroup(Guid id)
    {
        var group = Group(id);
        foreach (var criterion in Criteria.Where(x => x.GroupId == id).OrderBy(x => x.Position).ToList()) Reassign(criterion.Id, null);
        Groups.Remove(group);
        NormalizeGroups();
    }
    public Guid AddCriterion(CriterionInput input)
    {
        ValidateGroup(input.GroupId);
        var criterion = new Criterion(Id, input, Criteria.Count(x => x.GroupId == input.GroupId));
        Criteria.Add(criterion);
        return criterion.Id;
    }
    public void EditCriterion(Guid id, CriterionInput input)
    {
        var criterion = FindCriterion(id);
        ValidateGroup(input.GroupId);
        Reassign(id, input.GroupId);
        criterion.Update(input);
    }
    public void Reassign(Guid id, Guid? groupId)
    {
        ValidateGroup(groupId);
        var criterion = FindCriterion(id);
        if (criterion.GroupId == groupId) return;
        var old = criterion.GroupId;
        criterion.GroupId = groupId;
        criterion.Position = Criteria.Count(x => x.GroupId == groupId && x.Id != id);
        NormalizeCriteria(old);
    }
    public void MoveCriterion(Guid id, int direction)
    {
        var criterion = FindCriterion(id);
        Move(Criteria.Where(x => x.GroupId == criterion.GroupId).OrderBy(x => x.Position).ToList(), criterion, direction, (x, i) => x.Position = i);
    }
    public void PlaceCriterion(Guid id, Guid? groupId, int position)
    {
        ValidateGroup(groupId);
        var criterion = FindCriterion(id);
        var destination = Criteria.Where(x => x.GroupId == groupId && x.Id != id).OrderBy(x => x.Position).ToList();
        if (position < 0 || position > destination.Count) throw new RuleException("InvalidValue");
        var previousGroup = criterion.GroupId;
        criterion.GroupId = groupId;
        destination.Insert(position, criterion);
        for (var i = 0; i < destination.Count; i++) destination[i].Position = i;
        if (previousGroup != groupId) NormalizeCriteria(previousGroup);
    }
    public void RemoveCriterion(Guid id)
    {
        var criterion = FindCriterion(id);
        Criteria.Remove(criterion);
        NormalizeCriteria(criterion.GroupId);
    }
    public void Grant(string userId, AccessLevel access)
    {
        if (userId == OwnerId || access is not (AccessLevel.Reader or AccessLevel.Editor)) throw new RuleException("InvalidShare");
        var share = Shares.SingleOrDefault(x => x.UserId == userId);
        if (share is null) Shares.Add(new CaseShare { EvaluationId = Id, UserId = userId, Access = access });
        else share.Access = access;
    }
    public void Revoke(string userId) => Shares.RemoveAll(x => x.UserId == userId);
    private CriterionGroup Group(Guid id) => Groups.SingleOrDefault(x => x.Id == id) ?? throw new RuleException("NotFound");
    private Criterion FindCriterion(Guid id) => Criteria.SingleOrDefault(x => x.Id == id) ?? throw new RuleException("NotFound");
    private void ValidateGroup(Guid? id) { if (id is { } value) _ = Group(value); }
    private void NormalizeGroups() { var i = 0; foreach (var x in Groups.OrderBy(x => x.Position)) x.Position = i++; }
    private void NormalizeCriteria(Guid? group) { var i = 0; foreach (var x in Criteria.Where(x => x.GroupId == group).OrderBy(x => x.Position)) x.Position = i++; }
    private static void Move<T>(List<T> items, T item, int direction, Action<T, int> setPosition)
    {
        if (direction is not (-1 or 1)) throw new RuleException("InvalidValue");
        var current = items.IndexOf(item);
        var next = Math.Clamp(current + direction, 0, items.Count - 1);
        (items[current], items[next]) = (items[next], items[current]);
        for (var i = 0; i < items.Count; i++) setPosition(items[i], i);
    }
    private static void Place<T>(List<T> items, T item, int position, Action<T, int> setPosition)
    {
        if (position < 0 || position >= items.Count) throw new RuleException("InvalidValue");
        items.Remove(item);
        items.Insert(position, item);
        for (var i = 0; i < items.Count; i++) setPosition(items[i], i);
    }
}

public sealed class CriterionGroup
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EvaluationId { get; private set; }
    public string Name { get; internal set; } = "";
    public int Position { get; internal set; }
    private CriterionGroup() { }
    internal CriterionGroup(Guid evaluationId, string name, int position) => (EvaluationId, Name, Position) = (evaluationId, name, position);
}

public sealed record CriterionInput(string Name, CriterionType Type, Priority Priority, CriterionScope Scope, Guid? GroupId = null, decimal? Threshold = null, string? Unit = null, Comparison Comparison = Comparison.AtLeast);

public sealed class Criterion
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EvaluationId { get; private set; }
    public Guid? GroupId { get; internal set; }
    public string Name { get; private set; } = "";
    public CriterionType Type { get; private set; }
    public Priority Priority { get; private set; }
    public CriterionScope Scope { get; private set; }
    public decimal? Threshold { get; private set; }
    public string? Unit { get; private set; }
    public Comparison Comparison { get; private set; }
    public int Position { get; internal set; }
    private Criterion() { }
    internal Criterion(Guid evaluationId, CriterionInput input, int position)
    {
        EvaluationId = evaluationId; Position = position; GroupId = input.GroupId; Update(input);
    }
    internal void Update(CriterionInput input)
    {
        Name = Rules.Text(input.Name); Type = Rules.Defined(input.Type); Priority = Rules.Defined(input.Priority);
        Scope = Rules.Defined(input.Scope); Comparison = Rules.Defined(input.Comparison);
        if (Type == CriterionType.Measurement && (input.Threshold is null || string.IsNullOrWhiteSpace(input.Unit))) throw new RuleException("MeasurementRequired");
        Threshold = Type == CriterionType.Measurement ? input.Threshold : null;
        Unit = Type == CriterionType.Measurement ? Rules.Text(input.Unit, 30) : null;
    }
    public Fulfillment Assess(bool? boolean = null, decimal? measurement = null, int? rating = null)
    {
        if (Type == CriterionType.Boolean) return boolean is null ? Fulfillment.Unknown : boolean.Value ? Fulfillment.Met : Fulfillment.Unmet;
        if (Type == CriterionType.Measurement) return measurement is null ? Fulfillment.Unknown : (Comparison == Comparison.AtLeast ? measurement >= Threshold : measurement <= Threshold) ? Fulfillment.Met : Fulfillment.Unmet;
        if (rating is null) return Fulfillment.Unknown;
        if (rating is < 1 or > 5) throw new RuleException("InvalidValue");
        // A subjective rating is an observation, not an implicit pass/fail threshold.
        return Fulfillment.Rated;
    }
}

public sealed class CaseShare
{
    public Guid EvaluationId { get; set; }
    public string UserId { get; set; } = "";
    public AccessLevel Access { get; set; }
}
