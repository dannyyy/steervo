using Steervo.Core;
using Xunit;

namespace Steervo.Tests;

public sealed class OfferTests
{
    private static (Evaluation Evaluation, Guid Candidate) Create()
    {
        var evaluation = new Evaluation("owner", "Decision", null);
        return (evaluation, evaluation.AddCandidate(new("Example vehicle")));
    }
    [Fact]
    public void First_offer_sets_one_currency_and_invalid_input_does_not_lock_it()
    {
        var (evaluation, candidate) = Create();
        Assert.Throws<RuleException>(() => evaluation.AddOffer(candidate, new(), [], "EUR"));
        Assert.Null(evaluation.Currency);
        Assert.Throws<RuleException>(() => evaluation.AddOffer(candidate, new() { Name = "Request" }, [], "EU"));
        Assert.Throws<RuleException>(() => evaluation.AddOffer(Guid.NewGuid(), new() { Name = "Request" }, [], "EUR"));
        evaluation.AddOffer(candidate, new() { Name = "Request" }, [], " eur ");
        Assert.Equal("EUR", evaluation.Currency);
        Assert.Throws<RuleException>(() => evaluation.SetCurrency("USD"));
        Assert.Throws<RuleException>(() => evaluation.AddOffer(candidate, new() { Name = "Other" }, [], "USD"));
        evaluation.AddOffer(candidate, new() { Name = "Other" }, []);
        Assert.Equal(2, evaluation.Offers.Count);
    }
    [Fact]
    public void Zero_and_included_are_known_while_missing_terms_and_prices_stay_open()
    {
        var (evaluation, candidate) = Create();
        var id = evaluation.AddOffer(candidate, new() { Name = "Request", Kind = OfferKind.Leasing, MonthlyPayment = 0, AnnualInterestPercent = 0 },
            [new("Equipment", OfferLineKind.Equipment, OfferPriceKind.Included), new("Fee", OfferLineKind.Fee, OfferPriceKind.Amount, 0), new("Accessory")], "EUR");
        var revision = evaluation.FindOffer(id).Latest;
        Assert.DoesNotContain(nameof(OfferTerms.MonthlyPayment), revision.Terms.MissingFields());
        Assert.DoesNotContain(nameof(OfferTerms.AnnualInterestPercent), revision.Terms.MissingFields());
        Assert.Contains(nameof(OfferTerms.ExcessKilometrePrice), revision.Terms.MissingFields());
        Assert.Equal(OfferPriceKind.Included, revision.Lines[0].PriceKind);
        Assert.Equal(0, revision.Lines[1].Amount);
        Assert.Null(revision.Lines[2].Amount);
    }
    [Fact]
    public void Revisions_keep_previous_terms_and_line_items_unchanged()
    {
        var (evaluation, candidate) = Create();
        var id = evaluation.AddOffer(candidate, new() { Name = "Request", MonthlyPayment = 640 }, [new("Fee", OfferLineKind.Fee, OfferPriceKind.Amount, 275)], "EUR");
        var first = evaluation.FindOffer(id).Latest;
        evaluation.ReviseOffer(id, first.Terms with { MonthlyPayment = 610 }, [new("Fee", OfferLineKind.Fee, OfferPriceKind.Included)], "Negotiated");
        var offer = evaluation.FindOffer(id);
        Assert.Equal(640, first.Terms.MonthlyPayment);
        Assert.Equal(275, first.Lines.Single().Amount);
        Assert.Equal(610, offer.Latest.Terms.MonthlyPayment);
        Assert.NotEqual(first.Id, offer.Latest.Id);
        Assert.NotEqual(first.Lines.Single().Id, offer.Latest.Lines.Single().Id);
        Assert.Equal(2, offer.Latest.Number);
        Assert.Throws<RuleException>(() => evaluation.ReviseOffer(id, offer.Latest.Terms, offer.Latest.Lines.Select(line => line.ToInput())));
        Assert.Equal(2, offer.Revisions.Count);
    }
    [Fact]
    public void New_version_keeps_assessment_but_requires_review_before_it_counts_as_current()
    {
        var (evaluation, candidate) = Create();
        var boolean = evaluation.AddCriterion(new("Purchase option", CriterionType.Boolean, Priority.Required, CriterionScope.Offer));
        var rating = evaluation.AddCriterion(new("Communication", CriterionType.Rating, Priority.Required, CriterionScope.Offer));
        var id = evaluation.AddOffer(candidate, new() { Name = "Request" }, [], "EUR");
        evaluation.AssessOffer(id, boolean, new(BooleanValue: false));
        evaluation.AssessOffer(id, rating, new(RatingValue: 1));
        var assessmentId = evaluation.OfferAssessments.First().Id;
        Assert.Equal(Fulfillment.Unmet, evaluation.OfferResult(id, boolean));
        Assert.Equal(Fulfillment.Rated, evaluation.OfferResult(id, rating));
        evaluation.ReviseOffer(id, new() { Name = "Negotiated" }, []);
        Assert.Equal(Fulfillment.Unknown, evaluation.OfferResult(id, boolean));
        Assert.False(evaluation.OfferAssessments.First().BooleanValue);
        evaluation.AssessOffer(id, boolean, new(BooleanValue: true));
        Assert.Equal(assessmentId, evaluation.OfferAssessments.First().Id);
        Assert.Equal(2, evaluation.OfferAssessments.First().RevisionNumber);
        Assert.Equal(Fulfillment.Met, evaluation.OfferResult(id, boolean));
        Assert.Equal(Fulfillment.Unknown, evaluation.OfferResult(id, rating));
        Assert.Throws<RuleException>(() => evaluation.EditCriterion(boolean, new("Purchase option", CriterionType.Rating, Priority.Required, CriterionScope.Offer)));
        evaluation.ClearOfferAssessment(id, boolean);
        evaluation.EditCriterion(boolean, new("Purchase option", CriterionType.Rating, Priority.Required, CriterionScope.Offer));
    }
    [Fact]
    public void Candidate_copy_excludes_offers_and_archiving_is_reversible()
    {
        var (evaluation, candidate) = Create();
        var id = evaluation.AddOffer(candidate, new() { Name = "Request" }, [], "EUR");
        var clone = evaluation.CopyCandidate(candidate, "Other configuration");
        Assert.DoesNotContain(evaluation.Offers, offer => offer.CandidateId == clone);
        var criterion = evaluation.AddCriterion(new("Option", CriterionType.Boolean, Priority.Required, CriterionScope.Offer));
        evaluation.AssessOffer(id, criterion, new(BooleanValue: true));
        evaluation.ArchiveOffer(id, true);
        Assert.Throws<RuleException>(() => evaluation.ClearOfferAssessment(id, criterion));
        Assert.Throws<RuleException>(() => evaluation.ReviseOffer(id, new() { Name = "Changed" }, []));
        evaluation.ArchiveOffer(id, false);
        evaluation.ReviseOffer(id, new() { Name = "Changed" }, []);
        Assert.Equal(2, evaluation.FindOffer(id).Revisions.Count);
        evaluation.RemoveCandidate(candidate);
        Assert.Empty(evaluation.Offers);
        Assert.Single(evaluation.Candidates);
    }
    [Fact]
    public void Invalid_terms_lines_and_cross_scope_values_do_not_create_partial_revisions()
    {
        var (evaluation, candidate) = Create();
        var id = evaluation.AddOffer(candidate, new() { Name = "Request" }, [], "EUR");
        var vehicleCriterion = evaluation.AddCriterion(new("Feature", CriterionType.Boolean, Priority.Required, CriterionScope.Vehicle));
        Assert.Throws<RuleException>(() => evaluation.AssessOffer(id, vehicleCriterion, new(BooleanValue: true)));
        Assert.Throws<RuleException>(() => evaluation.ReviseOffer(id, new() { Name = "Invalid", MonthlyPayment = -1 }, []));
        Assert.Throws<RuleException>(() => evaluation.ReviseOffer(id, new() { Name = "Invalid", TermMonths = 0 }, []));
        Assert.Throws<RuleException>(() => evaluation.ReviseOffer(id, new() { Name = "Invalid", ValidUntil = new(2020, 1, 1), IssuedOn = new(2020, 2, 1) }, []));
        OfferLineInput[] invalid = [new(""), new("Included", PriceKind: OfferPriceKind.Included, Amount: 1), new("Unknown", Amount: 0), new("Amount", PriceKind: OfferPriceKind.Amount), new("Not a benefit", Effect: OfferBenefitEffect.Right), new("Right", Kind: OfferLineKind.Benefit, Effect: OfferBenefitEffect.Right, CostCategory: CostCategory.Service)];
        foreach (var line in invalid) Assert.Throws<RuleException>(() => evaluation.ReviseOffer(id, new() { Name = "Changed" }, [line]));
        Assert.Single(evaluation.FindOffer(id).Revisions);
        Assert.Empty(evaluation.OfferAssessments);
    }
}
