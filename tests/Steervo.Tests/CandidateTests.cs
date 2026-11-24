using Steervo.Core;
using Xunit;

namespace Steervo.Tests;

/// <summary>
/// Checks how cars are assessed against criteria, copied, classified and edited.
/// </summary>
public sealed class CandidateTests
{
    [Fact]
    public void Missing_values_are_unknown_while_false_and_zero_are_actual_observations()
    {
        var evaluation = new Evaluation("owner", "Decision", null);
        var candidate = evaluation.AddCandidate(new("Example model"));
        var boolean = evaluation.AddCriterion(
            new("Feature", CriterionType.Boolean, Priority.Required, CriterionScope.Vehicle)
        );
        var measurement = evaluation.AddCriterion(
            new(
                "Size",
                CriterionType.Measurement,
                Priority.Required,
                CriterionScope.Vehicle,
                Threshold: 0,
                Unit: "units",
                Comparison: Comparison.AtMost
            )
        );
        var rating = evaluation.AddCriterion(
            new("Impression", CriterionType.Rating, Priority.Required, CriterionScope.Vehicle)
        );

        Assert.Equal(Fulfillment.Unknown, evaluation.ResultFor(candidate, boolean));

        evaluation.AssessCandidate(candidate, boolean, new(BooleanValue: false));
        evaluation.AssessCandidate(candidate, measurement, new(MeasurementValue: 0));
        evaluation.AssessCandidate(
            candidate,
            rating,
            new(RatingValue: 1, Source: AssessmentSource.Inspection, Notes: "Personal observation")
        );

        Assert.Equal(Fulfillment.Unmet, evaluation.ResultFor(candidate, boolean));
        Assert.Equal(Fulfillment.Met, evaluation.ResultFor(candidate, measurement));
        Assert.Equal(Fulfillment.Rated, evaluation.ResultFor(candidate, rating));
        Assert.Equal(CandidateStatus.Watching, evaluation.FindCandidate(candidate).Status);

        evaluation.ClearAssessment(candidate, rating);

        Assert.Equal(Fulfillment.Unknown, evaluation.ResultFor(candidate, rating));
    }

    [Fact]
    public void Criterion_reordering_and_group_removal_keep_assessments_but_type_changes_require_resetting_values()
    {
        var evaluation = new Evaluation("owner", "Decision", null);
        var first = evaluation.AddGroup("First");
        var second = evaluation.AddGroup("Second");
        var criterion = evaluation.AddCriterion(
            new(
                "Size",
                CriterionType.Measurement,
                Priority.Required,
                CriterionScope.Vehicle,
                first,
                3,
                "units"
            )
        );
        var candidate = evaluation.AddCandidate(new("Example model"));
        evaluation.AssessCandidate(candidate, criterion, new(MeasurementValue: 4));
        var assessmentId = evaluation.Assessments.Single().Id;

        // Moving the criterion and removing its group leaves the assessment alone.
        evaluation.PlaceCriterion(criterion, second, 0);
        evaluation.RemoveGroup(second);

        Assert.Equal(assessmentId, evaluation.Assessments.Single().Id);
        Assert.Equal(Fulfillment.Met, evaluation.ResultFor(candidate, criterion));

        // A stricter threshold re-evaluates the stored value.
        evaluation.EditCriterion(
            criterion,
            new(
                "Size renamed",
                CriterionType.Measurement,
                Priority.Required,
                CriterionScope.Vehicle,
                first,
                5,
                "units"
            )
        );

        Assert.Equal(Fulfillment.Unmet, evaluation.ResultFor(candidate, criterion));

        // Type and unit cannot change while values of the old kind exist.
        Assert.Throws<RuleException>(() =>
            evaluation.EditCriterion(
                criterion,
                new("Boolean", CriterionType.Boolean, Priority.Required, CriterionScope.Vehicle)
            )
        );
        Assert.Throws<RuleException>(() =>
            evaluation.EditCriterion(
                criterion,
                new(
                    "Size",
                    CriterionType.Measurement,
                    Priority.Required,
                    CriterionScope.Vehicle,
                    Threshold: 5,
                    Unit: "other"
                )
            )
        );
        Assert.Equal(first, evaluation.Criteria.Single().GroupId);

        // Once the value is cleared, the type may change.
        evaluation.ClearAssessment(candidate, criterion);
        evaluation.EditCriterion(
            criterion,
            new("Boolean", CriterionType.Boolean, Priority.Required, CriterionScope.Vehicle)
        );

        Assert.Equal(Fulfillment.Unknown, evaluation.ResultFor(candidate, criterion));
    }

    [Fact]
    public void Assessments_validate_their_type_scope_and_resource_membership()
    {
        var evaluation = new Evaluation("owner", "Decision", null);
        var candidate = evaluation.AddCandidate(new("Example model"));
        var boolean = evaluation.AddCriterion(
            new("Feature", CriterionType.Boolean, Priority.Required, CriterionScope.Vehicle)
        );
        var rating = evaluation.AddCriterion(
            new("Experience", CriterionType.Rating, Priority.Required, CriterionScope.Vehicle)
        );
        var offerCriterion = evaluation.AddCriterion(
            new("Offer term", CriterionType.Boolean, Priority.Required, CriterionScope.Offer)
        );

        // The value must match the type of the criterion and the rating scale.
        Assert.Throws<RuleException>(() =>
            evaluation.AssessCandidate(candidate, boolean, new(MeasurementValue: 1))
        );
        Assert.Throws<RuleException>(() =>
            evaluation.AssessCandidate(candidate, rating, new(RatingValue: 0))
        );
        Assert.Throws<RuleException>(() =>
            evaluation.AssessCandidate(candidate, rating, new(RatingValue: 6))
        );

        // A car cannot answer a criterion that applies to offers.
        Assert.Throws<RuleException>(() =>
            evaluation.AssessCandidate(candidate, offerCriterion, new(BooleanValue: true))
        );

        // Car and criterion must belong to this evaluation.
        Assert.Throws<RuleException>(() =>
            evaluation.AssessCandidate(Guid.NewGuid(), boolean, new(BooleanValue: true))
        );
        Assert.Throws<RuleException>(() =>
            evaluation.AssessCandidate(candidate, Guid.NewGuid(), new(BooleanValue: true))
        );

        Assert.Empty(evaluation.Assessments);
    }

    [Fact]
    public void Candidate_copies_are_independent_and_exclusion_is_explicit_and_reasoned()
    {
        var evaluation = new Evaluation("owner", "Decision", null);
        var candidate = evaluation.AddCandidate(
            new(
                "Original",
                "Example",
                "Variant",
                CandidateCondition.Used,
                2023,
                1000,
                "Notes",
                "https://example.test/details"
            )
        );
        var criterion = evaluation.AddCriterion(
            new("Feature", CriterionType.Boolean, Priority.Required, CriterionScope.Vehicle)
        );
        evaluation.AssessCandidate(
            candidate,
            criterion,
            new(BooleanValue: true, Source: AssessmentSource.Research, Notes: "Checked")
        );

        // Excluding a car requires a reason.
        Assert.Throws<RuleException>(() =>
            evaluation.ClassifyCandidate(candidate, CandidateStatus.Excluded, " ")
        );
        evaluation.ClassifyCandidate(candidate, CandidateStatus.Excluded, "Personal choice");

        // A copy takes over notes and assessments, but starts with a fresh status.
        var copy = evaluation.CopyCandidate(candidate, "Another model");

        Assert.Equal(CandidateStatus.Watching, evaluation.FindCandidate(copy).Status);
        Assert.Equal("", evaluation.FindCandidate(copy).StatusReason);
        Assert.Equal("Notes", evaluation.FindCandidate(copy).Notes);
        Assert.Equal(Fulfillment.Met, evaluation.ResultFor(copy, criterion));

        // Changing the copy does not touch the original.
        evaluation.AssessCandidate(copy, criterion, new(BooleanValue: false));

        Assert.Equal(Fulfillment.Met, evaluation.ResultFor(candidate, criterion));

        evaluation.MoveCandidate(copy, -1);

        Assert.Equal(copy, evaluation.Candidates.OrderBy(c => c.Position).First().Id);

        // Assessments disappear together with their car or their criterion.
        evaluation.RemoveCandidate(candidate);

        Assert.Equal(copy, evaluation.Assessments.Single().CandidateId);

        evaluation.RemoveCriterion(criterion);

        Assert.Empty(evaluation.Assessments);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///tmp/example")]
    [InlineData("https://user:secret@example.test")]
    [InlineData("not a link")]
    public void Unsafe_source_links_are_rejected_without_partial_edits(string link)
    {
        var evaluation = new Evaluation("owner", "Decision", null);
        var id = evaluation.AddCandidate(new("Original"));

        Assert.Throws<RuleException>(() =>
            evaluation.EditCandidate(id, new("Changed", Links: link))
        );

        Assert.Equal("Original", evaluation.FindCandidate(id).Model);
    }

    [Fact]
    public void Removing_an_unknown_car_is_rejected_and_changes_nothing()
    {
        // Arrange: two cars with one offer each.
        var evaluation = new Evaluation("owner", "Decision", null);
        var first = evaluation.AddCandidate(new("First"));
        var second = evaluation.AddCandidate(new("Second"));
        var firstOffer = evaluation.AddOffer(first, new() { Name = "First offer" }, [], "EUR");
        var secondOffer = evaluation.AddOffer(second, new() { Name = "Second offer" }, []);

        // Act
        var error = Assert.Throws<RuleException>(() => evaluation.RemoveCandidate(Guid.NewGuid()));

        // Assert
        Assert.Equal("NotFound", error.Message);
        Assert.Equal([first, second], evaluation.Candidates.Select(candidate => candidate.Id));
        Assert.Equal([firstOffer, secondOffer], evaluation.Offers.Select(offer => offer.Id));

        // A known car is still removed together with its offer.
        evaluation.RemoveCandidate(first);

        Assert.Equal(second, evaluation.Candidates.Single().Id);
        Assert.Equal(secondOffer, evaluation.Offers.Single().Id);
    }
}
