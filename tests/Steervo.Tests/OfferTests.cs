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
    public void Candidate_copy_excludes_offers_and_removing_an_offer_removes_its_records()
    {
        var (evaluation, candidate) = Create();
        var id = evaluation.AddOffer(candidate, new() { Name = "Request" }, [], "EUR");
        var clone = evaluation.CopyCandidate(candidate, "Other configuration");
        Assert.DoesNotContain(evaluation.Offers, offer => offer.CandidateId == clone);
        var criterion = evaluation.AddCriterion(new("Option", CriterionType.Boolean, Priority.Required, CriterionScope.Offer));
        evaluation.AssessOffer(id, criterion, new(BooleanValue: true));
        var kept = evaluation.AddOffer(candidate, new() { Name = "Kept" }, []);
        var last = evaluation.AddOffer(clone, new() { Name = "Last" }, []);
        evaluation.RemoveOffer(id);
        Assert.Empty(evaluation.OfferAssessments);
        Assert.Equal([0, 1], evaluation.Offers.Select(offer => offer.Position));
        Assert.Throws<RuleException>(() => evaluation.RemoveOffer(id));
        Assert.Throws<RuleException>(() => evaluation.ReviseOffer(id, new() { Name = "Changed" }, []));
        evaluation.RemoveCandidate(candidate);
        Assert.Equal(last, evaluation.Offers.Single().Id); Assert.Equal(0, evaluation.Offers.Single().Position);
        Assert.DoesNotContain(evaluation.Offers, offer => offer.Id == kept);
    }
    [Fact]
    public void Offers_keep_a_manual_order_within_all_offers_or_one_car()
    {
        var (evaluation, candidate) = Create(); var other = evaluation.AddCandidate(new("Other vehicle"));
        var a = evaluation.AddOffer(candidate, new() { Name = "A" }, [], "EUR"); var b = evaluation.AddOffer(other, new() { Name = "B" }, []);
        var c = evaluation.AddOffer(candidate, new() { Name = "C" }, []);
        string[] Order() => evaluation.Offers.OrderBy(offer => offer.Position).Select(offer => offer.Latest.Terms.Name).ToArray();
        evaluation.MoveOffer(c, -1); Assert.Equal(["A", "C", "B"], Order());
        evaluation.MoveOffer(a, 1, candidate); Assert.Equal(["C", "A", "B"], Order());
        evaluation.MoveOffer(c, -1); evaluation.MoveOffer(b, 1); evaluation.MoveOffer(b, 1, candidate); Assert.Equal(["C", "A", "B"], Order());
        Assert.Throws<RuleException>(() => evaluation.MoveOffer(a, 2));
        Assert.Throws<RuleException>(() => evaluation.MoveOffer(Guid.NewGuid(), 1));
    }
    [Fact]
    public void Lines_add_up_to_the_total_the_contract_basis_and_the_concession()
    {
        var (evaluation, candidate) = Create();
        OfferLineInput[] lines = [new("Car", OfferLineKind.Vehicle, OfferPriceKind.Amount, 50000), new("Paint", OfferLineKind.Equipment, OfferPriceKind.Amount, 2000),
            new("Interior", OfferLineKind.Equipment, OfferPriceKind.Amount, 5000), new("Delivery", OfferLineKind.Fee, OfferPriceKind.Amount, 3000, Financed: false),
            new("Fleet", OfferLineKind.Discount, OfferPriceKind.Amount, 2500), new("Winter wheels", OfferLineKind.Benefit, OfferPriceKind.Included, Value: 1800),
            new("Tow bar", OfferLineKind.Accessory)];
        var id = evaluation.AddOffer(candidate, new() { Name = "Leasing", Kind = OfferKind.Leasing, StatedTotal = 57000 }, lines, "CHF");
        var pricing = evaluation.FindOffer(id).Latest.Pricing;
        Assert.Equal(57000, pricing.Vehicle); Assert.Equal(3000, pricing.Extras); Assert.Equal(60000, pricing.BeforeDiscounts);
        Assert.Equal(57500, pricing.Total); Assert.Equal(54500, pricing.Financed); Assert.Equal(3000, pricing.DueAtHandover);
        Assert.Equal(4300, pricing.Concession); Assert.Equal(7.2m, pricing.ConcessionPercent); Assert.Equal(1, pricing.Open);
        Assert.Equal(500, pricing.Difference(57000)); Assert.Null(pricing.Difference(57500)); Assert.Null(pricing.Difference(null));
        Assert.DoesNotContain("Price", evaluation.FindOffer(id).Latest.MissingFields());
        var empty = evaluation.FindOffer(evaluation.AddOffer(candidate, new() { Name = "Open" }, [new("Fleet", OfferLineKind.Discount, OfferPriceKind.Amount, 100)])).Latest;
        Assert.Contains("Price", empty.MissingFields()); Assert.Null(empty.Pricing.ConcessionPercent);
        Assert.False(OfferPricing.From([new("Open")]).Known); Assert.Null(OfferPricing.From([new("Open")]).Difference(10));
    }
    [Fact]
    public void A_price_list_without_the_vehicle_price_has_no_total()
    {
        OfferLineInput[] extras = [new("Vehicle", OfferLineKind.Vehicle), new("Paint", OfferLineKind.Accessory, OfferPriceKind.Amount, 1300), new("Fee", OfferLineKind.Fee, OfferPriceKind.Amount, 990)];
        var pricing = OfferPricing.From(extras);
        Assert.True(pricing.VehicleOpen); Assert.False(pricing.Known); Assert.Null(pricing.Difference(50000)); Assert.Equal(1, pricing.Open);
        // An open accessory still leaves a total; only the missing vehicle price withholds it.
        var known = OfferPricing.From([new("Vehicle", OfferLineKind.Vehicle, OfferPriceKind.Amount, 50000), new("Paint", OfferLineKind.Accessory)]);
        Assert.True(known.Known); Assert.Equal(50000, known.Total); Assert.False(known.VehicleOpen);
    }
    [Fact]
    public void An_offer_names_seller_lender_and_intermediary_separately()
    {
        var evaluation = new Evaluation("owner", "Roles", null); var candidate = evaluation.AddCandidate(new("Car"));
        var id = evaluation.AddOffer(candidate, new() { Name = "Lease", Provider = " Dealer ", Lender = "Bank", Intermediary = " Broker " }, [], "CHF");
        var terms = evaluation.FindOffer(id).Latest.Terms;
        Assert.Equal(("Dealer", "Bank", "Broker"), (terms.Provider, terms.Lender, terms.Intermediary));
        Assert.Throws<RuleException>(() => evaluation.AddOffer(candidate, new() { Name = "Long", Intermediary = new string('x', 201) }, [], "CHF"));
    }
    [Fact]
    public void A_line_fulfils_a_linked_requirement_until_an_own_answer_or_a_removed_criterion_replaces_it()
    {
        var (evaluation, candidate) = Create();
        var wheels = evaluation.AddCriterion(new("Winter wheels", CriterionType.Boolean, Priority.Required, CriterionScope.Offer));
        var rating = evaluation.AddCriterion(new("Service", CriterionType.Rating, Priority.Preferred, CriterionScope.Offer));
        var vehicle = evaluation.AddCriterion(new("Tow bar", CriterionType.Boolean, Priority.Required, CriterionScope.Vehicle));
        var id = evaluation.AddOffer(candidate, new() { Name = "Request" }, [new("Wheels", OfferLineKind.Benefit, OfferPriceKind.Included, CriterionId: wheels)], "EUR");
        Assert.Equal(Fulfillment.Met, evaluation.OfferResult(id, wheels));
        evaluation.AssessOffer(id, wheels, new(BooleanValue: false)); Assert.Equal(Fulfillment.Unmet, evaluation.OfferResult(id, wheels));
        evaluation.ClearOfferAssessment(id, wheels); Assert.Equal(Fulfillment.Met, evaluation.OfferResult(id, wheels));
        foreach (var invalid in new[] { rating, vehicle, Guid.NewGuid(), Guid.Empty })
            Assert.Throws<RuleException>(() => evaluation.ReviseOffer(id, new() { Name = "Request" }, [new("Wheels", CriterionId: invalid)]));
        Assert.Throws<RuleException>(() => evaluation.EditCriterion(wheels, new("Winter wheels", CriterionType.Rating, Priority.Required, CriterionScope.Offer)));
        Assert.Throws<RuleException>(() => evaluation.EditCriterion(wheels, new("Winter wheels", CriterionType.Boolean, Priority.Required, CriterionScope.Vehicle)));
        evaluation.ReviseOffer(id, new() { Name = "Request" }, [], "Wheels dropped"); Assert.Equal(Fulfillment.Unknown, evaluation.OfferResult(id, wheels));
        // Stored versions stay untouched; the export leaves the deleted requirement out.
        evaluation.RemoveCriterion(wheels);
        Assert.Equal(wheels, evaluation.FindOffer(id).Revision(1).Lines.Single().CriterionId);
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
        OfferLineInput[] invalid = [new(""), new("Included", PriceKind: OfferPriceKind.Included, Amount: 1), new("Unknown", Amount: 0), new("Amount", PriceKind: OfferPriceKind.Amount), new("Not a benefit", Effect: OfferBenefitEffect.Right), new("Right", Kind: OfferLineKind.Benefit, Effect: OfferBenefitEffect.Right, CostCategory: CostCategory.Service),
            new("Value", PriceKind: OfferPriceKind.Amount, Amount: 1, Value: 1), new("Value", PriceKind: OfferPriceKind.Included, Value: -1), new("Discount", OfferLineKind.Discount, OfferPriceKind.Included)];
        foreach (var line in invalid) Assert.Throws<RuleException>(() => evaluation.ReviseOffer(id, new() { Name = "Changed" }, [line]));
        Assert.Single(evaluation.FindOffer(id).Revisions);
        Assert.Empty(evaluation.OfferAssessments);
    }
}
