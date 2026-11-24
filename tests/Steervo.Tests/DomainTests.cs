using Steervo.Core;
using Xunit;

namespace Steervo.Tests;

/// <summary>
/// Checks the basic rules of an evaluation: ordering of groups and criteria, fulfilment and access.
/// </summary>
public sealed class DomainTests
{
    [Fact]
    public void Placement_supports_arbitrary_positions_and_cross_group_moves_without_changing_ids()
    {
        var evaluation = new Evaluation("owner", "Test decision", null);
        var first = evaluation.AddGroup("First");
        var second = evaluation.AddGroup("Second");
        var empty = evaluation.AddGroup("Empty");
        var criteria = Enumerable
            .Range(0, 4)
            .Select(number => AddBooleanCriterion(evaluation, $"Criterion {number}", first))
            .ToArray();

        // A group can be placed first and last.
        evaluation.PlaceGroup(empty, 0);
        Assert.Equal([empty, first, second], GroupIdsInOrder(evaluation));

        evaluation.PlaceGroup(empty, 2);
        Assert.Equal([first, second, empty], GroupIdsInOrder(evaluation));

        // Criteria can be placed anywhere inside their group.
        evaluation.PlaceCriterion(criteria[3], first, 0);
        evaluation.PlaceCriterion(criteria[0], first, 3);
        Assert.Equal(
            [criteria[3], criteria[1], criteria[2], criteria[0]],
            evaluation.Criteria.OrderBy(c => c.Position).Select(c => c.Id)
        );

        // Criteria can move into another group and out of every group.
        evaluation.PlaceCriterion(criteria[1], empty, 0);
        evaluation.PlaceCriterion(criteria[2], empty, 0);
        Assert.Equal(
            [criteria[2], criteria[1]],
            evaluation
                .Criteria.Where(c => c.GroupId == empty)
                .OrderBy(c => c.Position)
                .Select(c => c.Id)
        );

        evaluation.PlaceCriterion(criteria[1], null, 0);
        Assert.Null(evaluation.Criteria.Single(c => c.Id == criteria[1]).GroupId);

        // Positions stay gapless inside every group, and no criterion got a new id.
        foreach (var criteriaOfGroup in evaluation.Criteria.GroupBy(c => c.GroupId))
        {
            Assert.Equal(
                Enumerable.Range(0, criteriaOfGroup.Count()),
                criteriaOfGroup.OrderBy(c => c.Position).Select(c => c.Position)
            );
        }

        Assert.Equal(criteria.Order(), evaluation.Criteria.Select(c => c.Id).Order());
    }

    [Fact]
    public void Invalid_drop_targets_do_not_partially_change_the_order_or_group()
    {
        var evaluation = new Evaluation("owner", "Test decision", null);
        var first = evaluation.AddGroup("First");
        var empty = evaluation.AddGroup("Empty");
        var id = AddBooleanCriterion(evaluation, "Criterion", first);

        // Unknown group, unknown criterion and positions outside the target are rejected.
        Assert.Throws<RuleException>(() => evaluation.PlaceCriterion(id, Guid.NewGuid(), 0));
        Assert.Throws<RuleException>(() => evaluation.PlaceCriterion(Guid.NewGuid(), empty, 0));
        Assert.Throws<RuleException>(() => evaluation.PlaceCriterion(id, empty, 1));
        Assert.Throws<RuleException>(() => evaluation.PlaceCriterion(id, first, -1));
        Assert.Throws<RuleException>(() => evaluation.PlaceGroup(first, 2));
        Assert.Throws<RuleException>(() => evaluation.PlaceGroup(first, -1));

        Assert.Equal(first, evaluation.Criteria.Single().GroupId);
        Assert.Equal(0, evaluation.Criteria.Single().Position);
        Assert.Equal(0, evaluation.Groups.Single(g => g.Id == first).Position);
    }

    [Fact]
    public void Reordering_and_removing_groups_preserve_criterion_identity()
    {
        var evaluation = new Evaluation("owner", "Test decision", null);
        var first = evaluation.AddGroup("First");
        var second = evaluation.AddGroup("Second");
        var criterion = AddBooleanCriterion(evaluation, "Visibility", first);
        var other = evaluation.AddCriterion(
            new("Comfort", CriterionType.Rating, Priority.Preferred, CriterionScope.Vehicle, first)
        );

        evaluation.MoveCriterion(other, -1);

        Assert.Equal(other, evaluation.Criteria.OrderBy(c => c.Position).First().Id);

        // The criterion survives its group being renamed, moved and removed.
        evaluation.Reassign(criterion, second);
        evaluation.RenameGroup(second, "Renamed");
        evaluation.MoveGroup(second, -1);
        evaluation.RemoveGroup(second);

        Assert.Equal(criterion, evaluation.Criteria.Single(c => c.Name == "Visibility").Id);
        Assert.Null(evaluation.Criteria.Single(c => c.Id == criterion).GroupId);
        Assert.Equal(first, evaluation.Groups.Single().Id);
    }

    [Fact]
    public void Criteria_can_only_join_groups_in_their_own_case()
    {
        var evaluation = new Evaluation("owner", "Test decision", null);
        var unknownGroup = Guid.NewGuid();

        Assert.Throws<RuleException>(() => AddBooleanCriterion(evaluation, "A", unknownGroup));
    }

    [Fact]
    public void Unknown_is_distinct_from_unmet_and_zero_is_a_known_measurement()
    {
        var evaluation = new Evaluation("owner", "Test decision", null);
        evaluation.AddCriterion(
            new("A", CriterionType.Boolean, Priority.Required, CriterionScope.Vehicle)
        );
        evaluation.AddCriterion(
            new(
                "B",
                CriterionType.Measurement,
                Priority.Preferred,
                CriterionScope.Offer,
                Threshold: 0,
                Unit: "units",
                Comparison: Comparison.AtMost
            )
        );
        var boolean = evaluation.Criteria[0];
        var measurement = evaluation.Criteria[1];

        Assert.Equal(Fulfillment.Unknown, boolean.Assess());
        Assert.Equal(Fulfillment.Unmet, boolean.Assess(boolean: false));
        Assert.Equal(Fulfillment.Met, measurement.Assess(measurement: 0));
        Assert.Equal(Fulfillment.Unknown, measurement.Assess());
    }

    [Fact]
    public void A_known_subjective_rating_is_not_reported_as_unknown_or_converted_to_a_score()
    {
        var evaluation = new Evaluation("owner", "Test decision", null);
        evaluation.AddCriterion(
            new("Comfort", CriterionType.Rating, Priority.Preferred, CriterionScope.Vehicle)
        );

        Assert.Equal(Fulfillment.Unknown, evaluation.Criteria.Single().Assess());
        Assert.Equal(Fulfillment.Rated, evaluation.Criteria.Single().Assess(rating: 3));
        Assert.Throws<RuleException>(() => evaluation.Criteria.Single().Assess(rating: 6));
    }

    [Fact]
    public void Permissions_are_explicit_and_ownership_cannot_be_granted_as_a_share()
    {
        var evaluation = new Evaluation("owner", "Test decision", null);

        Assert.Null(evaluation.AccessFor("administrator"));

        evaluation.Grant("reader", AccessLevel.Reader);
        evaluation.Grant("editor", AccessLevel.Editor);

        Assert.Throws<RuleException>(() => evaluation.Require("reader", AccessLevel.Editor));
        Assert.Throws<RuleException>(() => evaluation.Require("editor", AccessLevel.Owner));
        Assert.Throws<RuleException>(() => evaluation.Grant("editor", AccessLevel.Owner));

        evaluation.Revoke("reader");

        Assert.Null(evaluation.AccessFor("reader"));
    }

    [Fact]
    public void Measurements_require_a_threshold_and_unit_and_enums_are_validated()
    {
        var evaluation = new Evaluation("owner", "Test decision", null);

        Assert.Throws<RuleException>(() =>
            evaluation.AddCriterion(
                new("A", CriterionType.Measurement, Priority.Required, CriterionScope.Vehicle)
            )
        );
        Assert.Throws<RuleException>(() =>
            evaluation.AddCriterion(
                new("A", (CriterionType)99, Priority.Required, CriterionScope.Vehicle)
            )
        );
        Assert.Throws<RuleException>(() => evaluation.AddGroup(" "));
    }

    [Fact]
    public void A_rejected_rename_leaves_name_and_purpose_unchanged()
    {
        const int maximumPurposeLength = 2000;
        var evaluation = new Evaluation("owner", "Family car", "Daily use");
        var tooLongPurpose = new string('x', maximumPurposeLength + 1);

        Assert.Throws<RuleException>(() => evaluation.Rename("Renamed", tooLongPurpose));
        Assert.Throws<RuleException>(() => evaluation.Rename(" ", "Other use"));

        Assert.Equal("Family car", evaluation.Name);
        Assert.Equal("Daily use", evaluation.Purpose);

        // A valid rename is trimmed and applied as a whole.
        evaluation.Rename(" Renamed ", new string('x', maximumPurposeLength));

        Assert.Equal("Renamed", evaluation.Name);
        Assert.Equal(maximumPurposeLength, evaluation.Purpose.Length);
    }

    [Fact]
    public void A_rejected_criterion_edit_leaves_the_criterion_and_its_group_unchanged()
    {
        const int maximumUnitLength = 30;
        var evaluation = new Evaluation("owner", "Test decision", null);
        var first = evaluation.AddGroup("First");
        var second = evaluation.AddGroup("Second");
        var id = AddBooleanCriterion(evaluation, "Visibility", first);
        var withoutUnit = new CriterionInput(
            "Range",
            CriterionType.Measurement,
            Priority.Optional,
            CriterionScope.Offer,
            second,
            Threshold: 400
        );
        var withTooLongUnit = withoutUnit with { Unit = new string('k', maximumUnitLength + 1) };
        var withUnknownComparison = withoutUnit with { Unit = "km", Comparison = (Comparison)99 };

        Assert.Throws<RuleException>(() => evaluation.EditCriterion(id, withoutUnit));
        Assert.Throws<RuleException>(() => evaluation.EditCriterion(id, withTooLongUnit));
        Assert.Throws<RuleException>(() => evaluation.EditCriterion(id, withUnknownComparison));

        var criterion = evaluation.Criteria.Single();
        Assert.Equal("Visibility", criterion.Name);
        Assert.Equal(CriterionType.Boolean, criterion.Type);
        Assert.Equal(Priority.Required, criterion.Priority);
        Assert.Equal(CriterionScope.Vehicle, criterion.Scope);
        Assert.Equal(first, criterion.GroupId);
        Assert.Null(criterion.Threshold);
        Assert.Null(criterion.Unit);

        // A valid edit changes the values and the group together.
        evaluation.EditCriterion(id, withoutUnit with { Unit = " km " });

        Assert.Equal("Range", criterion.Name);
        Assert.Equal(CriterionType.Measurement, criterion.Type);
        Assert.Equal(second, criterion.GroupId);
        Assert.Equal(400, criterion.Threshold);
        Assert.Equal("km", criterion.Unit);
    }

    private static Guid AddBooleanCriterion(Evaluation evaluation, string name, Guid group)
    {
        return evaluation.AddCriterion(
            new(name, CriterionType.Boolean, Priority.Required, CriterionScope.Vehicle, group)
        );
    }

    private static IEnumerable<Guid> GroupIdsInOrder(Evaluation evaluation)
    {
        return evaluation.Groups.OrderBy(g => g.Position).Select(g => g.Id);
    }
}
