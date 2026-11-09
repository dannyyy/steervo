using Steervo.Core;
using Xunit;

namespace Steervo.Tests;

public sealed class DomainTests
{
    [Fact]
    public void Placement_supports_arbitrary_positions_and_cross_group_moves_without_changing_ids()
    {
        var evaluation = new Evaluation("owner", "Test decision", null);
        var first = evaluation.AddGroup("First");
        var second = evaluation.AddGroup("Second");
        var empty = evaluation.AddGroup("Empty");
        var ids = Enumerable.Range(0, 4).Select(i => evaluation.AddCriterion(new($"Criterion {i}", CriterionType.Boolean, Priority.Required, CriterionScope.Vehicle, first))).ToArray();
        evaluation.PlaceGroup(empty, 0);
        Assert.Equal([empty, first, second], evaluation.Groups.OrderBy(x => x.Position).Select(x => x.Id));
        evaluation.PlaceGroup(empty, 2);
        Assert.Equal([first, second, empty], evaluation.Groups.OrderBy(x => x.Position).Select(x => x.Id));
        evaluation.PlaceCriterion(ids[3], first, 0);
        evaluation.PlaceCriterion(ids[0], first, 3);
        Assert.Equal([ids[3], ids[1], ids[2], ids[0]], evaluation.Criteria.OrderBy(x => x.Position).Select(x => x.Id));
        evaluation.PlaceCriterion(ids[1], empty, 0);
        evaluation.PlaceCriterion(ids[2], empty, 0);
        Assert.Equal([ids[2], ids[1]], evaluation.Criteria.Where(x => x.GroupId == empty).OrderBy(x => x.Position).Select(x => x.Id));
        evaluation.PlaceCriterion(ids[1], null, 0);
        Assert.Null(evaluation.Criteria.Single(x => x.Id == ids[1]).GroupId);
        foreach (var group in evaluation.Criteria.GroupBy(x => x.GroupId)) Assert.Equal(Enumerable.Range(0, group.Count()), group.OrderBy(x => x.Position).Select(x => x.Position));
        Assert.Equal(ids.Order(), evaluation.Criteria.Select(x => x.Id).Order());
    }

    [Fact]
    public void Invalid_drop_targets_do_not_partially_change_the_order_or_group()
    {
        var evaluation = new Evaluation("owner", "Test decision", null);
        var first = evaluation.AddGroup("First");
        var empty = evaluation.AddGroup("Empty");
        var id = evaluation.AddCriterion(new("Criterion", CriterionType.Boolean, Priority.Required, CriterionScope.Vehicle, first));
        Assert.Throws<RuleException>(() => evaluation.PlaceCriterion(id, Guid.NewGuid(), 0));
        Assert.Throws<RuleException>(() => evaluation.PlaceCriterion(Guid.NewGuid(), empty, 0));
        Assert.Throws<RuleException>(() => evaluation.PlaceCriterion(id, empty, 1));
        Assert.Throws<RuleException>(() => evaluation.PlaceCriterion(id, first, -1));
        Assert.Throws<RuleException>(() => evaluation.PlaceGroup(first, 2));
        Assert.Throws<RuleException>(() => evaluation.PlaceGroup(first, -1));
        Assert.Equal(first, evaluation.Criteria.Single().GroupId);
        Assert.Equal(0, evaluation.Criteria.Single().Position);
        Assert.Equal(0, evaluation.Groups.Single(x => x.Id == first).Position);
    }

    [Fact]
    public void Reordering_and_removing_groups_preserve_criterion_identity()
    {
        var evaluation = new Evaluation("owner", "Test decision", null);
        var first = evaluation.AddGroup("First");
        var second = evaluation.AddGroup("Second");
        var criterion = evaluation.AddCriterion(new("Visibility", CriterionType.Boolean, Priority.Required, CriterionScope.Vehicle, first));
        var other = evaluation.AddCriterion(new("Comfort", CriterionType.Rating, Priority.Preferred, CriterionScope.Vehicle, first));
        evaluation.MoveCriterion(other, -1);
        Assert.Equal(other, evaluation.Criteria.OrderBy(x => x.Position).First().Id);
        evaluation.Reassign(criterion, second);
        evaluation.RenameGroup(second, "Renamed");
        evaluation.MoveGroup(second, -1);
        evaluation.RemoveGroup(second);
        Assert.Equal(criterion, evaluation.Criteria.Single(x => x.Name == "Visibility").Id);
        Assert.Null(evaluation.Criteria.Single(x => x.Id == criterion).GroupId);
        Assert.Equal(first, evaluation.Groups.Single().Id);
    }

    [Fact]
    public void Criteria_can_only_join_groups_in_their_own_case()
    {
        var evaluation = new Evaluation("owner", "Test decision", null);
        Assert.Throws<RuleException>(() => evaluation.AddCriterion(new("A", CriterionType.Boolean, Priority.Required, CriterionScope.Vehicle, Guid.NewGuid())));
    }

    [Fact]
    public void Unknown_is_distinct_from_unmet_and_zero_is_a_known_measurement()
    {
        var evaluation = new Evaluation("owner", "Test decision", null);
        evaluation.AddCriterion(new("A", CriterionType.Boolean, Priority.Required, CriterionScope.Vehicle));
        evaluation.AddCriterion(new("B", CriterionType.Measurement, Priority.Preferred, CriterionScope.Offer, Threshold: 0, Unit: "units", Comparison: Comparison.AtMost));
        Assert.Equal(Fulfillment.Unknown, evaluation.Criteria[0].Assess());
        Assert.Equal(Fulfillment.Unmet, evaluation.Criteria[0].Assess(boolean: false));
        Assert.Equal(Fulfillment.Met, evaluation.Criteria[1].Assess(measurement: 0));
        Assert.Equal(Fulfillment.Unknown, evaluation.Criteria[1].Assess());
    }

    [Fact]
    public void A_known_subjective_rating_is_not_reported_as_unknown_or_converted_to_a_score()
    {
        var evaluation = new Evaluation("owner", "Test decision", null);
        evaluation.AddCriterion(new("Comfort", CriterionType.Rating, Priority.Preferred, CriterionScope.Vehicle));
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
        Assert.Throws<RuleException>(() => evaluation.AddCriterion(new("A", CriterionType.Measurement, Priority.Required, CriterionScope.Vehicle)));
        Assert.Throws<RuleException>(() => evaluation.AddCriterion(new("A", (CriterionType)99, Priority.Required, CriterionScope.Vehicle)));
        Assert.Throws<RuleException>(() => evaluation.AddGroup(" "));
    }
}
