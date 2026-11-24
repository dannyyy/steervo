using Steervo.Core;
using Xunit;

namespace Steervo.Tests;

public sealed class OfferTests
{
    private static (Evaluation Evaluation, Guid Candidate) CreateEvaluationWithCandidate()
    {
        var evaluation = new Evaluation("owner", "Decision", null);
        var candidate = evaluation.AddCandidate(new("Example vehicle"));

        return (evaluation, candidate);
    }

    [Fact]
    public void First_offer_sets_one_currency_and_invalid_input_does_not_lock_it()
    {
        var (evaluation, candidate) = CreateEvaluationWithCandidate();

        // Rejected offers leave the currency open.
        Assert.Throws<RuleException>(() => evaluation.AddOffer(candidate, new(), [], "EUR"));
        Assert.Null(evaluation.Currency);
        Assert.Throws<RuleException>(() =>
            evaluation.AddOffer(candidate, new() { Name = "Request" }, [], "EU")
        );
        Assert.Throws<RuleException>(() =>
            evaluation.AddOffer(Guid.NewGuid(), new() { Name = "Request" }, [], "EUR")
        );

        // The first accepted offer fixes it for the whole evaluation.
        evaluation.AddOffer(candidate, new() { Name = "Request" }, [], " eur ");
        Assert.Equal("EUR", evaluation.Currency);
        Assert.Throws<RuleException>(() => evaluation.SetCurrency("USD"));
        Assert.Throws<RuleException>(() =>
            evaluation.AddOffer(candidate, new() { Name = "Other" }, [], "USD")
        );

        // Further offers need no currency of their own.
        evaluation.AddOffer(candidate, new() { Name = "Other" }, []);
        Assert.Equal(2, evaluation.Offers.Count);
    }

    [Fact]
    public void Zero_and_included_are_known_while_missing_terms_and_prices_stay_open()
    {
        // Arrange
        var (evaluation, candidate) = CreateEvaluationWithCandidate();
        var terms = new OfferTerms
        {
            Name = "Request",
            Kind = OfferKind.Leasing,
            MonthlyPayment = 0,
            AnnualInterestPercent = 0,
        };
        OfferLineInput[] lines =
        [
            new("Equipment", OfferLineKind.Equipment, OfferPriceKind.Included),
            new("Fee", OfferLineKind.Fee, OfferPriceKind.Amount, 0),
            new("Accessory"),
        ];

        // Act
        var offerId = evaluation.AddOffer(candidate, terms, lines, "EUR");

        // Assert
        var revision = evaluation.FindOffer(offerId).Latest;
        Assert.DoesNotContain(nameof(OfferTerms.MonthlyPayment), revision.Terms.MissingFields());
        Assert.DoesNotContain(
            nameof(OfferTerms.AnnualInterestPercent),
            revision.Terms.MissingFields()
        );
        Assert.Contains(nameof(OfferTerms.ExcessKilometrePrice), revision.Terms.MissingFields());
        Assert.Equal(OfferPriceKind.Included, revision.Lines[0].PriceKind);
        Assert.Equal(0, revision.Lines[1].Amount);
        Assert.Null(revision.Lines[2].Amount);
    }

    [Fact]
    public void Revisions_keep_previous_terms_and_line_items_unchanged()
    {
        // Arrange
        var (evaluation, candidate) = CreateEvaluationWithCandidate();
        var offerId = evaluation.AddOffer(
            candidate,
            new() { Name = "Request", MonthlyPayment = 640 },
            [new("Fee", OfferLineKind.Fee, OfferPriceKind.Amount, 275)],
            "EUR"
        );
        var first = evaluation.FindOffer(offerId).Latest;

        // Act
        evaluation.ReviseOffer(
            offerId,
            first.Terms with
            {
                MonthlyPayment = 610,
            },
            [new("Fee", OfferLineKind.Fee, OfferPriceKind.Included)],
            "Negotiated"
        );

        // Assert: the first version is untouched, the second one is a record of its own.
        var offer = evaluation.FindOffer(offerId);
        Assert.Equal(640, first.Terms.MonthlyPayment);
        Assert.Equal(275, first.Lines.Single().Amount);
        Assert.Equal(610, offer.Latest.Terms.MonthlyPayment);
        Assert.NotEqual(first.Id, offer.Latest.Id);
        Assert.NotEqual(first.Lines.Single().Id, offer.Latest.Lines.Single().Id);
        Assert.Equal(2, offer.Latest.Number);

        // A revision that changes nothing does not create a version.
        Assert.Throws<RuleException>(() =>
            evaluation.ReviseOffer(
                offerId,
                offer.Latest.Terms,
                offer.Latest.Lines.Select(line => line.ToInput())
            )
        );
        Assert.Equal(2, offer.Revisions.Count);
    }

    [Fact]
    public void New_version_keeps_assessment_but_requires_review_before_it_counts_as_current()
    {
        // Arrange: an offer assessed against a yes/no and a rating criterion.
        var (evaluation, candidate) = CreateEvaluationWithCandidate();
        var boolean = evaluation.AddCriterion(
            new("Purchase option", CriterionType.Boolean, Priority.Required, CriterionScope.Offer)
        );
        var rating = evaluation.AddCriterion(
            new("Communication", CriterionType.Rating, Priority.Required, CriterionScope.Offer)
        );
        var offerId = evaluation.AddOffer(candidate, new() { Name = "Request" }, [], "EUR");
        evaluation.AssessOffer(offerId, boolean, new(BooleanValue: false));
        evaluation.AssessOffer(offerId, rating, new(RatingValue: 1));
        var assessmentId = evaluation.OfferAssessments.First().Id;
        Assert.Equal(Fulfillment.Unmet, evaluation.OfferResult(offerId, boolean));
        Assert.Equal(Fulfillment.Rated, evaluation.OfferResult(offerId, rating));

        // A new version keeps the stored answer but no longer counts it.
        evaluation.ReviseOffer(offerId, new() { Name = "Negotiated" }, []);
        Assert.Equal(Fulfillment.Unknown, evaluation.OfferResult(offerId, boolean));
        Assert.False(evaluation.OfferAssessments.First().BooleanValue);

        // Answering again updates the same assessment for the new version; the rating stays open.
        evaluation.AssessOffer(offerId, boolean, new(BooleanValue: true));
        Assert.Equal(assessmentId, evaluation.OfferAssessments.First().Id);
        Assert.Equal(2, evaluation.OfferAssessments.First().RevisionNumber);
        Assert.Equal(Fulfillment.Met, evaluation.OfferResult(offerId, boolean));
        Assert.Equal(Fulfillment.Unknown, evaluation.OfferResult(offerId, rating));

        // The criterion type can only change once no answer depends on it.
        Assert.Throws<RuleException>(() =>
            evaluation.EditCriterion(
                boolean,
                new(
                    "Purchase option",
                    CriterionType.Rating,
                    Priority.Required,
                    CriterionScope.Offer
                )
            )
        );
        evaluation.ClearOfferAssessment(offerId, boolean);
        evaluation.EditCriterion(
            boolean,
            new("Purchase option", CriterionType.Rating, Priority.Required, CriterionScope.Offer)
        );
    }

    [Fact]
    public void Candidate_copy_excludes_offers_and_removing_an_offer_removes_its_records()
    {
        var (evaluation, candidate) = CreateEvaluationWithCandidate();
        var offerId = evaluation.AddOffer(candidate, new() { Name = "Request" }, [], "EUR");

        // Copying the car does not copy its offers.
        var clone = evaluation.CopyCandidate(candidate, "Other configuration");
        Assert.DoesNotContain(evaluation.Offers, offer => offer.CandidateId == clone);

        // Removing an offer removes its assessments and closes the gap in the order.
        var criterion = evaluation.AddCriterion(
            new("Option", CriterionType.Boolean, Priority.Required, CriterionScope.Offer)
        );
        evaluation.AssessOffer(offerId, criterion, new(BooleanValue: true));
        var kept = evaluation.AddOffer(candidate, new() { Name = "Kept" }, []);
        var last = evaluation.AddOffer(clone, new() { Name = "Last" }, []);

        evaluation.RemoveOffer(offerId);

        Assert.Empty(evaluation.OfferAssessments);
        Assert.Equal([0, 1], evaluation.Offers.Select(offer => offer.Position));
        Assert.Throws<RuleException>(() => evaluation.RemoveOffer(offerId));
        Assert.Throws<RuleException>(() =>
            evaluation.ReviseOffer(offerId, new() { Name = "Changed" }, [])
        );

        // Removing the car removes its remaining offers; the offer of the copy moves up.
        evaluation.RemoveCandidate(candidate);

        Assert.Equal(last, evaluation.Offers.Single().Id);
        Assert.Equal(0, evaluation.Offers.Single().Position);
        Assert.DoesNotContain(evaluation.Offers, offer => offer.Id == kept);
    }

    [Fact]
    public void Offers_keep_a_manual_order_within_all_offers_or_one_car()
    {
        // Arrange: offers A and C belong to the first car, B to the other one.
        var (evaluation, candidate) = CreateEvaluationWithCandidate();
        var other = evaluation.AddCandidate(new("Other vehicle"));
        var offerA = evaluation.AddOffer(candidate, new() { Name = "A" }, [], "EUR");
        var offerB = evaluation.AddOffer(other, new() { Name = "B" }, []);
        var offerC = evaluation.AddOffer(candidate, new() { Name = "C" }, []);

        string[] OfferNamesInOrder()
        {
            return evaluation
                .Offers.OrderBy(offer => offer.Position)
                .Select(offer => offer.Latest.Terms.Name)
                .ToArray();
        }

        // Moving within all offers.
        evaluation.MoveOffer(offerC, -1);
        Assert.Equal(["A", "C", "B"], OfferNamesInOrder());

        // Moving within the offers of one car skips the offers of other cars.
        evaluation.MoveOffer(offerA, 1, candidate);
        Assert.Equal(["C", "A", "B"], OfferNamesInOrder());

        // Moves that have nowhere to go change nothing.
        evaluation.MoveOffer(offerC, -1);
        evaluation.MoveOffer(offerB, 1);
        evaluation.MoveOffer(offerB, 1, candidate);
        Assert.Equal(["C", "A", "B"], OfferNamesInOrder());

        Assert.Throws<RuleException>(() => evaluation.MoveOffer(offerA, 2));
        Assert.Throws<RuleException>(() => evaluation.MoveOffer(Guid.NewGuid(), 1));
    }

    [Fact]
    public void Lines_add_up_to_the_total_the_contract_basis_and_the_concession()
    {
        // Arrange
        var (evaluation, candidate) = CreateEvaluationWithCandidate();
        OfferLineInput[] lines =
        [
            new("Car", OfferLineKind.Vehicle, OfferPriceKind.Amount, 50000),
            new("Paint", OfferLineKind.Equipment, OfferPriceKind.Amount, 2000),
            new("Interior", OfferLineKind.Equipment, OfferPriceKind.Amount, 5000),
            new("Delivery", OfferLineKind.Fee, OfferPriceKind.Amount, 3000, Financed: false),
            new("Fleet", OfferLineKind.Discount, OfferPriceKind.Amount, 2500),
            new("Winter wheels", OfferLineKind.Benefit, OfferPriceKind.Included, Value: 1800),
            new("Tow bar", OfferLineKind.Accessory),
        ];
        var terms = new OfferTerms
        {
            Name = "Leasing",
            Kind = OfferKind.Leasing,
            StatedTotal = 57000,
        };

        // Act
        var offerId = evaluation.AddOffer(candidate, terms, lines, "CHF");

        // Assert
        var pricing = evaluation.FindOffer(offerId).Latest.Pricing;
        Assert.Equal(57000, pricing.Vehicle);
        Assert.Equal(3000, pricing.Extras);
        Assert.Equal(60000, pricing.BeforeDiscounts);
        Assert.Equal(57500, pricing.Total);
        Assert.Equal(54500, pricing.Financed);
        Assert.Equal(3000, pricing.DueAtHandover);
        Assert.Equal(4300, pricing.Concession);
        Assert.Equal(7.2m, pricing.ConcessionPercent);
        Assert.Equal(1, pricing.Open);
        Assert.Equal(500, pricing.Difference(57000));
        Assert.Null(pricing.Difference(57500));
        Assert.Null(pricing.Difference(null));
        Assert.DoesNotContain("Price", evaluation.FindOffer(offerId).Latest.MissingFields());

        // A discount alone is not a price.
        var discountOnlyId = evaluation.AddOffer(
            candidate,
            new() { Name = "Open" },
            [new("Fleet", OfferLineKind.Discount, OfferPriceKind.Amount, 100)]
        );
        var empty = evaluation.FindOffer(discountOnlyId).Latest;
        Assert.Contains("Price", empty.MissingFields());
        Assert.Null(empty.Pricing.ConcessionPercent);
        Assert.False(OfferPricing.From([new("Open")]).Known);
        Assert.Null(OfferPricing.From([new("Open")]).Difference(10));
    }

    [Fact]
    public void A_price_list_without_the_vehicle_price_has_no_total()
    {
        OfferLineInput[] extras =
        [
            new("Vehicle", OfferLineKind.Vehicle),
            new("Paint", OfferLineKind.Accessory, OfferPriceKind.Amount, 1300),
            new("Fee", OfferLineKind.Fee, OfferPriceKind.Amount, 990),
        ];

        var pricing = OfferPricing.From(extras);

        Assert.True(pricing.VehicleOpen);
        Assert.False(pricing.Known);
        Assert.Null(pricing.Difference(50000));
        Assert.Equal(1, pricing.Open);

        // An open accessory still leaves a total; only the missing vehicle price withholds it.
        var known = OfferPricing.From([
            new("Vehicle", OfferLineKind.Vehicle, OfferPriceKind.Amount, 50000),
            new("Paint", OfferLineKind.Accessory),
        ]);

        Assert.True(known.Known);
        Assert.Equal(50000, known.Total);
        Assert.False(known.VehicleOpen);
    }

    [Fact]
    public void The_instalment_check_tells_whether_price_down_payment_residual_and_rate_fit()
    {
        OfferLineInput[] lines =
        [
            new("Vehicle", OfferLineKind.Vehicle, OfferPriceKind.Amount, 57480),
            new("Bonus", OfferLineKind.Discount, OfferPriceKind.Amount, 4000),
        ];
        var terms = new OfferTerms
        {
            Name = "Lease",
            Kind = OfferKind.Leasing,
            TermMonths = 36,
            AnnualInterestPercent = 0,
            MonthlyPayment = 667,
            DownPayment = 250,
            ResidualPayment = 29205,
        };

        var check = InstalmentCheck.From(terms, OfferPricing.From(lines))!;

        Assert.Equal(667.36m, check.Expected);
        Assert.True(check.Consistent);

        // The bonus counted as the customer's own down payment no longer fits: 4'000 would be deducted twice.
        var twice = InstalmentCheck.From(
            terms with
            {
                DownPayment = 4250,
            },
            OfferPricing.From(lines)
        )!;

        Assert.False(twice.Consistent);
        Assert.Equal(556.25m, twice.Expected);
        Assert.Equal(110.75m, twice.Difference);

        // Interest: instalments at the start or the end of the month are both accepted, nothing else.
        var loan = new OfferTerms
        {
            Name = "Loan",
            Kind = OfferKind.Financing,
            TermMonths = 48,
            AnnualInterestPercent = 3.9m,
            DownPayment = 5000,
            ResidualPayment = 10000,
        };
        OfferLineInput[] car =
        [
            new("Vehicle", OfferLineKind.Vehicle, OfferPriceKind.Amount, 45000),
        ];
        var end = InstalmentCheck.Payment(40000, 10000, 48, 3.9m);
        var start = InstalmentCheck.Payment(40000, 10000, 48, 3.9m, inAdvance: true);

        Assert.InRange(end, 705, 712);
        Assert.True(start < end);
        Assert.True(
            InstalmentCheck
                .From(loan with { MonthlyPayment = end }, OfferPricing.From(car))!
                .Consistent
        );
        Assert.True(
            InstalmentCheck
                .From(loan with { MonthlyPayment = start }, OfferPricing.From(car))!
                .Consistent
        );
        Assert.False(
            InstalmentCheck
                .From(loan with { MonthlyPayment = end + 20 }, OfferPricing.From(car))!
                .Consistent
        );

        // Without every figure, for a cash purchase or without a vehicle price there is nothing to check.
        Assert.Null(
            InstalmentCheck.From(
                terms with
                {
                    AnnualInterestPercent = null,
                },
                OfferPricing.From(lines)
            )
        );
        Assert.Null(
            InstalmentCheck.From(terms with { ResidualPayment = null }, OfferPricing.From(lines))
        );
        Assert.Null(
            InstalmentCheck.From(terms with { Kind = OfferKind.Purchase }, OfferPricing.From(lines))
        );
        Assert.Null(
            InstalmentCheck.From(terms, OfferPricing.From([new("Vehicle", OfferLineKind.Vehicle)]))
        );
    }

    [Fact]
    public void An_offer_names_seller_lender_and_intermediary_separately()
    {
        // Arrange
        var evaluation = new Evaluation("owner", "Roles", null);
        var candidate = evaluation.AddCandidate(new("Car"));
        var parties = new OfferTerms
        {
            Name = "Lease",
            Provider = " Dealer ",
            Lender = "Bank",
            Intermediary = " Broker ",
        };

        // Act
        var offerId = evaluation.AddOffer(candidate, parties, [], "CHF");

        // Assert: the names are stored trimmed and each in its own field.
        var terms = evaluation.FindOffer(offerId).Latest.Terms;
        Assert.Equal(
            ("Dealer", "Bank", "Broker"),
            (terms.Provider, terms.Lender, terms.Intermediary)
        );
        Assert.Throws<RuleException>(() =>
            evaluation.AddOffer(
                candidate,
                new() { Name = "Long", Intermediary = new string('x', 201) },
                [],
                "CHF"
            )
        );
    }

    [Fact]
    public void A_line_fulfils_a_linked_requirement_until_an_own_answer_or_a_removed_criterion_replaces_it()
    {
        // Arrange: an offer whose benefit line is linked to the "Winter wheels" requirement.
        var (evaluation, candidate) = CreateEvaluationWithCandidate();
        var wheels = evaluation.AddCriterion(
            new("Winter wheels", CriterionType.Boolean, Priority.Required, CriterionScope.Offer)
        );
        var rating = evaluation.AddCriterion(
            new("Service", CriterionType.Rating, Priority.Preferred, CriterionScope.Offer)
        );
        var vehicle = evaluation.AddCriterion(
            new("Tow bar", CriterionType.Boolean, Priority.Required, CriterionScope.Vehicle)
        );
        var offerId = evaluation.AddOffer(
            candidate,
            new() { Name = "Request" },
            [new("Wheels", OfferLineKind.Benefit, OfferPriceKind.Included, CriterionId: wheels)],
            "EUR"
        );

        // The line fulfils the requirement unless an own answer says otherwise.
        Assert.Equal(Fulfillment.Met, evaluation.OfferResult(offerId, wheels));
        evaluation.AssessOffer(offerId, wheels, new(BooleanValue: false));
        Assert.Equal(Fulfillment.Unmet, evaluation.OfferResult(offerId, wheels));
        evaluation.ClearOfferAssessment(offerId, wheels);
        Assert.Equal(Fulfillment.Met, evaluation.OfferResult(offerId, wheels));

        // Only an existing yes/no requirement of an offer can be linked.
        foreach (var invalid in new[] { rating, vehicle, Guid.NewGuid(), Guid.Empty })
        {
            Assert.Throws<RuleException>(() =>
                evaluation.ReviseOffer(
                    offerId,
                    new() { Name = "Request" },
                    [new("Wheels", CriterionId: invalid)]
                )
            );
        }

        // A linked requirement cannot change its type or scope.
        Assert.Throws<RuleException>(() =>
            evaluation.EditCriterion(
                wheels,
                new("Winter wheels", CriterionType.Rating, Priority.Required, CriterionScope.Offer)
            )
        );
        Assert.Throws<RuleException>(() =>
            evaluation.EditCriterion(
                wheels,
                new(
                    "Winter wheels",
                    CriterionType.Boolean,
                    Priority.Required,
                    CriterionScope.Vehicle
                )
            )
        );

        // Without the line in the latest version the requirement is open again.
        evaluation.ReviseOffer(offerId, new() { Name = "Request" }, [], "Wheels dropped");
        Assert.Equal(Fulfillment.Unknown, evaluation.OfferResult(offerId, wheels));

        // Stored versions stay untouched; the export leaves the deleted requirement out.
        evaluation.RemoveCriterion(wheels);
        Assert.Equal(wheels, evaluation.FindOffer(offerId).Revision(1).Lines.Single().CriterionId);
    }

    [Fact]
    public void Invalid_terms_lines_and_cross_scope_values_do_not_create_partial_revisions()
    {
        // Arrange
        var (evaluation, candidate) = CreateEvaluationWithCandidate();
        var offerId = evaluation.AddOffer(candidate, new() { Name = "Request" }, [], "EUR");
        var vehicleCriterion = evaluation.AddCriterion(
            new("Feature", CriterionType.Boolean, Priority.Required, CriterionScope.Vehicle)
        );

        // A vehicle criterion cannot be answered for an offer.
        Assert.Throws<RuleException>(() =>
            evaluation.AssessOffer(offerId, vehicleCriterion, new(BooleanValue: true))
        );

        // Invalid terms.
        Assert.Throws<RuleException>(() =>
            evaluation.ReviseOffer(offerId, new() { Name = "Invalid", MonthlyPayment = -1 }, [])
        );
        Assert.Throws<RuleException>(() =>
            evaluation.ReviseOffer(offerId, new() { Name = "Invalid", TermMonths = 0 }, [])
        );
        Assert.Throws<RuleException>(() =>
            evaluation.ReviseOffer(
                offerId,
                new()
                {
                    Name = "Invalid",
                    ValidUntil = new(2020, 1, 1),
                    IssuedOn = new(2020, 2, 1),
                },
                []
            )
        );

        // Invalid lines, each rejected on its own.
        OfferLineInput[] invalid =
        [
            new(""),
            new("Included", PriceKind: OfferPriceKind.Included, Amount: 1),
            new("Unknown", Amount: 0),
            new("Amount", PriceKind: OfferPriceKind.Amount),
            new("Not a benefit", Effect: OfferBenefitEffect.Right),
            new(
                "Right",
                Kind: OfferLineKind.Benefit,
                Effect: OfferBenefitEffect.Right,
                CostCategory: CostCategory.Service
            ),
            new("Value", PriceKind: OfferPriceKind.Amount, Amount: 1, Value: 1),
            new("Value", PriceKind: OfferPriceKind.Included, Value: -1),
            new("Discount", OfferLineKind.Discount, OfferPriceKind.Included),
        ];
        foreach (var line in invalid)
        {
            Assert.Throws<RuleException>(() =>
                evaluation.ReviseOffer(offerId, new() { Name = "Changed" }, [line])
            );
        }

        // Nothing of all that was stored.
        Assert.Single(evaluation.FindOffer(offerId).Revisions);
        Assert.Empty(evaluation.OfferAssessments);
    }
}
