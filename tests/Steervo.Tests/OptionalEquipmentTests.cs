using Steervo.Core;
using Xunit;

namespace Steervo.Tests;

public sealed class OptionalEquipmentTests
{
    [Theory, InlineData(CriterionScope.Vehicle), InlineData(CriterionScope.Offer)]
    public void Optional_equipment_is_known_but_neither_met_nor_unmet(CriterionScope scope)
    {
        var evaluation = new Evaluation("owner", "Decision", null);
        var candidate = evaluation.AddCandidate(new("Vehicle"));
        var criterion = evaluation.AddCriterion(new("Physical key", CriterionType.Boolean, Priority.Required, scope));
        var offer = evaluation.AddOffer(candidate, new() { Name = "Offer" }, [], "EUR");
        void Assess(AssessmentInput input)
        {
            if (scope == CriterionScope.Vehicle) evaluation.AssessCandidate(candidate, criterion, input);
            else evaluation.AssessOffer(offer, criterion, input);
        }
        Fulfillment Result() => scope == CriterionScope.Vehicle ? evaluation.ResultFor(candidate, criterion) : evaluation.OfferResult(offer, criterion);
        Assert.Equal(Fulfillment.Unknown, Result());
        Assess(new(OptionalAvailable: true, Source: AssessmentSource.Manufacturer, Notes: "Available separately"));
        Assert.Equal(Fulfillment.Optional, Result());
        Assert.Equal(CandidateStatus.Watching, evaluation.FindCandidate(candidate).Status);
        Assert.True(scope == CriterionScope.Vehicle ? evaluation.Assessments.Single().HasValue : evaluation.OfferAssessments.Single().HasValue);
        Assert.Throws<RuleException>(() => evaluation.EditCriterion(criterion, new("Physical key", CriterionType.Rating, Priority.Required, scope)));
        foreach (var invalid in new[] { new AssessmentInput(BooleanValue: true, OptionalAvailable: true),
            new AssessmentInput(BooleanValue: false, OptionalAvailable: true), new AssessmentInput(MeasurementValue: 0, OptionalAvailable: true),
            new AssessmentInput(RatingValue: 3, OptionalAvailable: true) })
            Assert.Throws<RuleException>(() => Assess(invalid));
        Assert.Equal(Fulfillment.Optional, Result());
        Assess(new(BooleanValue: true)); Assert.Equal(Fulfillment.Met, Result());
        Assess(new(BooleanValue: false)); Assert.Equal(Fulfillment.Unmet, Result());
        Assess(new()); Assert.Equal(Fulfillment.Unknown, Result());
    }

    [Theory, InlineData(CriterionScope.Vehicle), InlineData(CriterionScope.Offer)]
    public void Optional_is_only_available_for_boolean_criteria(CriterionScope scope)
    {
        var evaluation = new Evaluation("owner", "Decision", null);
        var candidate = evaluation.AddCandidate(new("Vehicle"));
        var offer = evaluation.AddOffer(candidate, new() { Name = "Offer" }, [], "EUR");
        foreach (var type in new[] { CriterionType.Measurement, CriterionType.Rating })
        {
            var criterion = evaluation.AddCriterion(new("Requirement", type, Priority.Required, scope, Threshold: 5, Unit: "units"));
            Assert.Throws<RuleException>(() => {
                if (scope == CriterionScope.Vehicle) evaluation.AssessCandidate(candidate, criterion, new(OptionalAvailable: true));
                else evaluation.AssessOffer(offer, criterion, new(OptionalAvailable: true));
            });
        }
        Assert.Empty(evaluation.Assessments); Assert.Empty(evaluation.OfferAssessments);
    }

    [Fact]
    public void Copying_preserves_optional_equipment_and_new_offer_versions_require_review()
    {
        var evaluation = new Evaluation("owner", "Decision", null);
        var candidate = evaluation.AddCandidate(new("Vehicle"));
        var criterion = evaluation.AddCriterion(new("Physical key", CriterionType.Boolean, Priority.Required, CriterionScope.Vehicle));
        evaluation.AssessCandidate(candidate, criterion, new(OptionalAvailable: true, Notes: "Available separately"));
        var copy = evaluation.CopyCandidate(candidate, "Copy");
        Assert.Equal(Fulfillment.Optional, evaluation.ResultFor(copy, criterion));
        Assert.Equal("Available separately", evaluation.Assessments.Single(a => a.CandidateId == copy).Notes);
        evaluation.ClearAssessment(copy, criterion);
        Assert.Equal(Fulfillment.Unknown, evaluation.ResultFor(copy, criterion));
        Assert.Equal(Fulfillment.Optional, evaluation.ResultFor(candidate, criterion));
        var offerCriterion = evaluation.AddCriterion(new("Accessories", CriterionType.Boolean, Priority.Required, CriterionScope.Offer));
        var offer = evaluation.AddOffer(candidate, new() { Name = "Offer" }, [], "EUR");
        evaluation.AssessOffer(offer, offerCriterion, new(OptionalAvailable: true));
        evaluation.ReviseOffer(offer, new() { Name = "Revised" }, []);
        Assert.True(evaluation.OfferAssessments.Single().OptionalAvailable);
        Assert.Equal(Fulfillment.Unknown, evaluation.OfferResult(offer, offerCriterion));
        evaluation.AssessOffer(offer, offerCriterion, new(OptionalAvailable: true));
        Assert.Equal(Fulfillment.Optional, evaluation.OfferResult(offer, offerCriterion));
    }
}
