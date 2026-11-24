namespace Steervo.Core;

/// <summary>What a user may do with an evaluation; higher levels include the lower ones.</summary>
public enum AccessLevel
{
    Reader,
    Editor,
    Owner,
}

public enum CriterionType
{
    Boolean,
    Measurement,
    Rating,
}

public enum Priority
{
    Required,
    Preferred,
    Optional,
}

/// <summary>Whether a criterion is answered once per car or once per offer.</summary>
public enum CriterionScope
{
    Vehicle,
    Offer,
}

public enum Comparison
{
    AtLeast,
    AtMost,
}

public enum Fulfillment
{
    Unknown,
    Met,
    Unmet,
    Rated,
    Optional,
}

/// <summary>A violated domain rule. The message is a stable error code, not a text for the user.</summary>
public sealed class RuleException(string code) : Exception(code);

/// <summary>Validation shared by all domain types, together with the limits they have in common.</summary>
public static class Rules
{
    // A planning horizon or contract term of at most one hundred years.
    internal const int MaximumMonths = 1200;
    internal const decimal MaximumAmount = 1_000_000_000_000m;
    internal const int MaximumKilometresPerYear = 1_000_000;
    internal const int MinimumRating = 1;
    internal const int MaximumRating = 5;
    internal const int ShortNoteLength = 2000;
    internal const int LongNoteLength = 4000;

    // Offers and documents may be backdated, but not before this year and not into the future.
    internal const int EarliestRecordYear = 1900;
    internal const int ClockToleranceMinutes = 5;

    /// <summary>Trims a required text and rejects it when it is empty or longer than <paramref name="max"/>.</summary>
    public static string Text(string? value, int max = 160)
    {
        var result = value?.Trim() ?? "";

        if (result.Length is 0 || result.Length > max)
        {
            throw new RuleException("InvalidText");
        }

        return result;
    }

    public static T Defined<T>(T value)
        where T : struct, Enum =>
        Enum.IsDefined(value) ? value : throw new RuleException("InvalidValue");

    /// <summary>Trims an optional text; a missing value becomes an empty string.</summary>
    public static string OptionalText(string? value, int max)
    {
        var result = value?.Trim() ?? "";

        return result.Length <= max ? result : throw new RuleException("InvalidText");
    }

    internal static bool IsAcceptableRecordDate(DateTimeOffset createdAt)
    {
        var isTooEarly = createdAt.Year < EarliestRecordYear;

        return !isTooEarly && createdAt <= DateTimeOffset.UtcNow.AddMinutes(ClockToleranceMinutes);
    }
}

/// <summary>
/// One purchase decision: the criteria, the cars under consideration, their offers, costs and
/// scenarios. All changes go through this aggregate so that its rules hold together.
/// </summary>
public sealed partial class Evaluation
{
    private const int MaximumPurposeLength = 2000;

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

    /// <summary>The access a user has, or <c>null</c> when the evaluation is not shared with them.</summary>
    public AccessLevel? AccessFor(string userId)
    {
        if (OwnerId == userId)
        {
            return AccessLevel.Owner;
        }

        return Shares.SingleOrDefault(share => share.UserId == userId)?.Access;
    }

    public void Require(string userId, AccessLevel minimum)
    {
        if (AccessFor(userId) is not { } actual || actual < minimum)
        {
            throw new RuleException("Forbidden");
        }
    }

    public void Rename(string name, string? purpose)
    {
        // Both are validated first so that a rejected change leaves neither half-updated.
        var validName = Rules.Text(name);
        var validPurpose = Rules.OptionalText(purpose, MaximumPurposeLength);

        Name = validName;
        Purpose = validPurpose;
    }

    public Guid AddGroup(string name)
    {
        var group = new CriterionGroup(Id, Rules.Text(name), Groups.Count);
        Groups.Add(group);

        return group.Id;
    }

    public void RenameGroup(Guid id, string name) => Group(id).Name = Rules.Text(name);

    public void MoveGroup(Guid id, int direction)
    {
        var orderedGroups = Groups.OrderBy(group => group.Position).ToList();

        Move(orderedGroups, Group(id), direction, (group, position) => group.Position = position);
    }

    public void PlaceGroup(Guid id, int position)
    {
        var orderedGroups = Groups.OrderBy(group => group.Position).ToList();

        Place(orderedGroups, Group(id), position, (group, index) => group.Position = index);
    }

    /// <summary>Removes a group; its criteria stay and become ungrouped.</summary>
    public void RemoveGroup(Guid id)
    {
        var group = Group(id);
        var criteriaInGroup = Criteria
            .Where(criterion => criterion.GroupId == id)
            .OrderBy(criterion => criterion.Position)
            .ToList();

        foreach (var criterion in criteriaInGroup)
        {
            Reassign(criterion.Id, null);
        }

        Groups.Remove(group);
        NormalizeGroups();
    }

    public Guid AddCriterion(CriterionInput input)
    {
        ValidateGroup(input.GroupId);

        var positionInGroup = Criteria.Count(existing => existing.GroupId == input.GroupId);
        var criterion = new Criterion(Id, input, positionInGroup);
        Criteria.Add(criterion);

        return criterion.Id;
    }

    public void EditCriterion(Guid id, CriterionInput input)
    {
        var criterion = FindCriterion(id);
        ValidateGroup(input.GroupId);

        if (WouldInvalidateAnswers(criterion, input))
        {
            throw new RuleException("CriterionHasAssessments");
        }

        // The values are applied first: a rejected edit must not have reassigned the group.
        criterion.Update(input);
        Reassign(id, input.GroupId);
    }

    /// <summary>Moves a criterion to the end of another group, or out of all groups.</summary>
    public void Reassign(Guid id, Guid? groupId)
    {
        ValidateGroup(groupId);
        var criterion = FindCriterion(id);

        if (criterion.GroupId == groupId)
        {
            return;
        }

        var previousGroupId = criterion.GroupId;
        criterion.GroupId = groupId;
        criterion.Position = Criteria.Count(other => other.GroupId == groupId && other.Id != id);
        NormalizeCriteria(previousGroupId);
    }

    public void MoveCriterion(Guid id, int direction)
    {
        var criterion = FindCriterion(id);
        var criteriaInGroup = Criteria
            .Where(other => other.GroupId == criterion.GroupId)
            .OrderBy(other => other.Position)
            .ToList();

        Move(criteriaInGroup, criterion, direction, (moved, position) => moved.Position = position);
    }

    public void PlaceCriterion(Guid id, Guid? groupId, int position)
    {
        ValidateGroup(groupId);
        var criterion = FindCriterion(id);
        var destination = Criteria
            .Where(other => other.GroupId == groupId && other.Id != id)
            .OrderBy(other => other.Position)
            .ToList();

        if (position < 0 || position > destination.Count)
        {
            throw new RuleException("InvalidValue");
        }

        var previousGroupId = criterion.GroupId;
        criterion.GroupId = groupId;
        destination.Insert(position, criterion);

        for (var index = 0; index < destination.Count; index++)
        {
            destination[index].Position = index;
        }

        if (previousGroupId != groupId)
        {
            NormalizeCriteria(previousGroupId);
        }
    }

    public void RemoveCriterion(Guid id)
    {
        var criterion = FindCriterion(id);

        Assessments.RemoveAll(assessment => assessment.CriterionId == id);
        OfferAssessments.RemoveAll(assessment => assessment.CriterionId == id);
        Criteria.Remove(criterion);
        NormalizeCriteria(criterion.GroupId);
    }

    /// <summary>Shares the evaluation with another user, or changes the access they already have.</summary>
    public void Grant(string userId, AccessLevel access)
    {
        // Ownership is not shared: only reading and editing can be granted, and never to the owner.
        if (userId == OwnerId || access is not (AccessLevel.Reader or AccessLevel.Editor))
        {
            throw new RuleException("InvalidShare");
        }

        var share = Shares.SingleOrDefault(existing => existing.UserId == userId);

        if (share is null)
        {
            Shares.Add(
                new CaseShare
                {
                    EvaluationId = Id,
                    UserId = userId,
                    Access = access,
                }
            );
        }
        else
        {
            share.Access = access;
        }
    }

    public void Revoke(string userId) => Shares.RemoveAll(share => share.UserId == userId);

    private CriterionGroup Group(Guid id) =>
        Groups.SingleOrDefault(group => group.Id == id) ?? throw new RuleException("NotFound");

    private Criterion FindCriterion(Guid id) =>
        Criteria.SingleOrDefault(criterion => criterion.Id == id)
        ?? throw new RuleException("NotFound");

    private void ValidateGroup(Guid? id)
    {
        if (id is { } groupId)
        {
            _ = Group(groupId);
        }
    }

    // Existing answers and linked offer lines were given for a certain kind of criterion.
    // An edit must not silently change what they mean.
    private bool WouldInvalidateAnswers(Criterion criterion, CriterionInput input)
    {
        var id = criterion.Id;
        var scopeChanges = criterion.Scope != input.Scope;
        var typeChanges = criterion.Type != input.Type;
        var newUnit = input.Type == CriterionType.Measurement ? input.Unit?.Trim() : null;
        var unitChanges = criterion.Unit != newUnit;

        var isLinkedToOfferLine = Offers
            .SelectMany(offer => offer.Revisions)
            .SelectMany(revision => revision.Lines)
            .Any(line => line.CriterionId == id);
        var isAssessed =
            Assessments.Any(assessment => assessment.CriterionId == id)
            || OfferAssessments.Any(assessment => assessment.CriterionId == id);
        var hasAssessedValues =
            Assessments.Any(assessment => assessment.CriterionId == id && assessment.HasValue)
            || OfferAssessments.Any(assessment =>
                assessment.CriterionId == id && assessment.HasValue
            );

        var breaksOfferLineLinks = isLinkedToOfferLine && (scopeChanges || typeChanges);
        var detachesAssessments = scopeChanges && isAssessed;
        var reinterpretsValues = hasAssessedValues && (typeChanges || unitChanges);

        return breaksOfferLineLinks || detachesAssessments || reinterpretsValues;
    }

    private void NormalizeGroups()
    {
        var position = 0;

        foreach (var group in Groups.OrderBy(group => group.Position))
        {
            group.Position = position++;
        }
    }

    private void NormalizeCriteria(Guid? groupId)
    {
        var position = 0;
        var criteriaInGroup = Criteria
            .Where(criterion => criterion.GroupId == groupId)
            .OrderBy(criterion => criterion.Position);

        foreach (var criterion in criteriaInGroup)
        {
            criterion.Position = position++;
        }
    }

    // Swaps an item with its neighbour; at either end of the list it stays where it is.
    private static void Move<T>(List<T> items, T item, int direction, Action<T, int> setPosition)
    {
        if (direction is not (-1 or 1))
        {
            throw new RuleException("InvalidValue");
        }

        var current = items.IndexOf(item);
        var next = Math.Clamp(current + direction, 0, items.Count - 1);
        (items[current], items[next]) = (items[next], items[current]);

        Renumber(items, setPosition);
    }

    private static void Place<T>(List<T> items, T item, int position, Action<T, int> setPosition)
    {
        if (position < 0 || position >= items.Count)
        {
            throw new RuleException("InvalidValue");
        }

        items.Remove(item);
        items.Insert(position, item);

        Renumber(items, setPosition);
    }

    private static void Renumber<T>(List<T> items, Action<T, int> setPosition)
    {
        for (var index = 0; index < items.Count; index++)
        {
            setPosition(items[index], index);
        }
    }
}

public sealed class CriterionGroup
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EvaluationId { get; private set; }
    public string Name { get; internal set; } = "";
    public int Position { get; internal set; }

    private CriterionGroup() { }

    internal CriterionGroup(Guid evaluationId, string name, int position)
    {
        EvaluationId = evaluationId;
        Name = name;
        Position = position;
    }
}

public sealed record CriterionInput(
    string Name,
    CriterionType Type,
    Priority Priority,
    CriterionScope Scope,
    Guid? GroupId = null,
    decimal? Threshold = null,
    string? Unit = null,
    Comparison Comparison = Comparison.AtLeast
);

/// <summary>A requirement or wish that every car, or every offer, is checked against.</summary>
public sealed class Criterion
{
    private const int MaximumUnitLength = 30;

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
        EvaluationId = evaluationId;
        Position = position;
        GroupId = input.GroupId;
        Update(input);
    }

    internal void Update(CriterionInput input)
    {
        // Everything is validated first so that a rejected change leaves the criterion untouched.
        var name = Rules.Text(input.Name);
        var type = Rules.Defined(input.Type);
        var priority = Rules.Defined(input.Priority);
        var scope = Rules.Defined(input.Scope);
        var comparison = Rules.Defined(input.Comparison);

        var isMeasurement = type == CriterionType.Measurement;
        var lacksThresholdOrUnit = input.Threshold is null || string.IsNullOrWhiteSpace(input.Unit);

        if (isMeasurement && lacksThresholdOrUnit)
        {
            throw new RuleException("MeasurementRequired");
        }

        // Threshold and unit only mean something for a measurement; other types drop them.
        var unit = isMeasurement ? Rules.Text(input.Unit, MaximumUnitLength) : null;

        Name = name;
        Type = type;
        Priority = priority;
        Scope = scope;
        Comparison = comparison;
        Threshold = isMeasurement ? input.Threshold : null;
        Unit = unit;
    }

    /// <summary>
    /// Judges one answer against this criterion. Only the value matching the criterion type is
    /// considered; a missing value is <see cref="Fulfillment.Unknown"/>.
    /// </summary>
    public Fulfillment Assess(
        bool? boolean = null,
        decimal? measurement = null,
        int? rating = null,
        bool optionalAvailable = false
    )
    {
        return Type switch
        {
            CriterionType.Boolean => AssessBoolean(boolean, optionalAvailable),
            CriterionType.Measurement => AssessMeasurement(measurement),
            _ => AssessRating(rating),
        };
    }

    private static Fulfillment AssessBoolean(bool? boolean, bool optionalAvailable)
    {
        // "Available as an option" takes precedence over a plain yes or no.
        if (optionalAvailable)
        {
            return Fulfillment.Optional;
        }

        if (boolean is null)
        {
            return Fulfillment.Unknown;
        }

        return boolean.Value ? Fulfillment.Met : Fulfillment.Unmet;
    }

    private Fulfillment AssessMeasurement(decimal? measurement)
    {
        if (measurement is null)
        {
            return Fulfillment.Unknown;
        }

        var meetsThreshold =
            Comparison == Comparison.AtLeast ? measurement >= Threshold : measurement <= Threshold;

        return meetsThreshold ? Fulfillment.Met : Fulfillment.Unmet;
    }

    private static Fulfillment AssessRating(int? rating)
    {
        if (rating is null)
        {
            return Fulfillment.Unknown;
        }

        if (rating is < Rules.MinimumRating or > Rules.MaximumRating)
        {
            throw new RuleException("InvalidValue");
        }

        // A subjective rating is an observation, not an implicit pass/fail threshold.
        return Fulfillment.Rated;
    }
}

/// <summary>Access of one further user to an evaluation.</summary>
public sealed class CaseShare
{
    public Guid EvaluationId { get; set; }
    public string UserId { get; set; } = "";
    public AccessLevel Access { get; set; }
}
