using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Steervo.Application.Offers;
using Steervo.Core;
using Steervo.Infrastructure.Data;
using Xunit;

namespace Steervo.Tests;

[Collection("Postgres")]
public sealed class OfferPostgresTests
{
    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Revisions_are_persistent_append_only_authorized_and_versioned(int port)
    {
        // Arrange: an evaluation with one car, shared with a reader and an editor.
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync();
        var reader = await fixture.InviteAsync(owner, "reader@example.test");
        var editor = await fixture.InviteAsync(owner, "editor@example.test");
        var evaluationId = await fixture.AsAsync(
            owner,
            workspace => workspace.CreateAsync("Decision", null, "CH", "EUR")
        );

        Guid candidate = default;
        await fixture.ActAsync(
            owner,
            workspace =>
                workspace.EditAsync(
                    evaluationId,
                    1,
                    evaluation => candidate = evaluation.AddCandidate(new("Vehicle"))
                )
        );
        await fixture.ActAsync(
            owner,
            workspace =>
                workspace.ShareAsync(evaluationId, 2, "reader@example.test", AccessLevel.Reader)
        );
        await fixture.ActAsync(
            owner,
            workspace =>
                workspace.ShareAsync(evaluationId, 3, "editor@example.test", AccessLevel.Editor)
        );

        var terms = new OfferTerms
        {
            Name = "Request",
            Kind = OfferKind.Leasing,
            Provider = "Provider",
            MonthlyPayment = 640,
            AnnualInterestPercent = 2.75m,
            TermMonths = 24,
        };

        // A reader may not add an offer.
        await Assert.ThrowsAsync<RuleException>(() =>
            fixture.AsAsync(
                reader,
                workspace =>
                    new OfferWorkspace(workspace).AddAsync(
                        evaluationId,
                        4,
                        candidate,
                        terms,
                        [],
                        "EUR",
                        null
                    )
            )
        );

        // The editor adds the offer and then revises it.
        var offerId = await fixture.AsAsync(
            editor,
            workspace =>
                new OfferWorkspace(workspace).AddAsync(
                    evaluationId,
                    4,
                    candidate,
                    terms,
                    [new("Fee", OfferLineKind.Fee, OfferPriceKind.Amount, 275)],
                    "EUR",
                    null
                )
        );
        await fixture.ActAsync(
            editor,
            workspace =>
                new OfferWorkspace(workspace).ReviseAsync(
                    evaluationId,
                    5,
                    offerId,
                    terms with
                    {
                        MonthlyPayment = 610,
                    },
                    [new("Fee", OfferLineKind.Fee, OfferPriceKind.Included)],
                    "Negotiated"
                )
        );

        // Both versions are stored; the first one is unchanged.
        var saved = await fixture.AsAsync(reader, workspace => workspace.ReadAsync(evaluationId));
        var offer = saved.FindOffer(offerId);
        Assert.Equal("EUR", saved.Currency);
        Assert.Equal(640, offer.Revision(1).Terms.MonthlyPayment);
        Assert.Equal(275, offer.Revision(1).Lines.Single().Amount);
        Assert.Equal(610, offer.Latest.Terms.MonthlyPayment);

        // A revision based on an outdated evaluation version is rejected.
        await Assert.ThrowsAsync<RuleException>(() =>
            fixture.ActAsync(
                editor,
                workspace =>
                    new OfferWorkspace(workspace).ReviseAsync(
                        evaluationId,
                        5,
                        offerId,
                        terms,
                        [],
                        null
                    )
            )
        );

        // The database itself refuses to change a stored version or one of its lines.
        var revisionError = await Assert.ThrowsAsync<PostgresException>(() =>
            fixture.ExecuteSqlAsync(
                $"UPDATE \"OfferRevision\" SET \"Terms_MonthlyPayment\" = 1 WHERE \"Id\" = {offer.Revision(1).Id}"
            )
        );
        Assert.Equal(PostgresErrorCodes.CheckViolation, revisionError.SqlState);

        var lineError = await Assert.ThrowsAsync<PostgresException>(() =>
            fixture.ExecuteSqlAsync(
                $"UPDATE \"OfferLine\" SET \"Amount\" = 1 WHERE \"Id\" = {offer.Revision(1).Lines.Single().Id}"
            )
        );
        Assert.Equal(PostgresErrorCodes.CheckViolation, lineError.SqlState);

        // Once the share is revoked the former editor can no longer remove the offer.
        await fixture.ActAsync(
            owner,
            workspace => workspace.RevokeAsync(evaluationId, saved.Version, editor)
        );
        await Assert.ThrowsAsync<RuleException>(() =>
            fixture.ActAsync(
                editor,
                workspace =>
                    new OfferWorkspace(workspace).RemoveAsync(
                        evaluationId,
                        saved.Version + 1,
                        offerId
                    )
            )
        );
    }

    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Export_remaps_offer_history_linked_lines_and_order(int port)
    {
        // Arrange: three offers, the first with two versions, an assessment and a linked line.
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync();
        var evaluationId = await fixture.AsAsync(
            owner,
            workspace => workspace.CreateAsync("Decision", null, "CH", "EUR")
        );
        await fixture.ActAsync(
            owner,
            workspace => workspace.EditAsync(evaluationId, 1, AddOffersWithHistory)
        );
        var original = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(evaluationId));

        // Act: export the evaluation and import the file as a copy.
        var bytes = await fixture.TransferAsync(
            owner,
            transfer => transfer.ExportAsync(evaluationId)
        );
        var copyId = await fixture.TransferAsync(owner, transfer => transfer.ImportAsync(bytes));
        var copy = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(copyId));

        // The manual order and the currency survive.
        Assert.Equal(
            ["Negotiated", "Third", "Second"],
            copy.Offers.OrderBy(item => item.Position).Select(item => item.Latest.Terms.Name)
        );
        var offer = copy.Offers.Single(item => item.Latest.Terms.Name == "Negotiated");
        var originalOffer = original.Offers.Single(item => item.Latest.Terms.Name == "Negotiated");
        Assert.Equal("EUR", copy.Currency);

        // The linked line points at the copied criterion, not at the original one.
        var linked = offer.Latest.Lines.Single(line => line.CriterionId.HasValue);
        var wheels = copy.Criteria.Single(criterion => criterion.Name == "Wheels").Id;
        Assert.Equal(wheels, linked.CriterionId);
        Assert.NotEqual(
            original.Criteria.Single(criterion => criterion.Name == "Wheels").Id,
            linked.CriterionId
        );
        Assert.Equal(Fulfillment.Met, copy.OfferResult(offer.Id, wheels));

        // Prices and the whole history are carried over under new ids.
        Assert.Equal(27800, offer.Latest.Pricing.Total);
        Assert.Equal(800, offer.Latest.Pricing.DueAtHandover);
        Assert.False(offer.Latest.Lines.Single(line => line.Name == "Delivery").Financed);
        Assert.NotEqual(originalOffer.Id, offer.Id);
        Assert.NotEqual(originalOffer.CandidateId, offer.CandidateId);
        Assert.Equal(originalOffer.Revision(1).CreatedAt, offer.Revision(1).CreatedAt);
        Assert.Equal(originalOffer.Revision(1).Terms, offer.Revision(1).Terms);
        Assert.NotEqual(
            originalOffer.Revision(1).Lines.Single().Id,
            offer.Revision(1).Lines.Single().Id
        );
        Assert.Equal(
            originalOffer.Revision(1).Lines.Single().ToInput(),
            offer.Revision(1).Lines.Single().ToInput()
        );

        // The assessment still belongs to the first version, so it does not count for the latest.
        Assert.Equal(1, copy.OfferAssessments.Single().RevisionNumber);
        Assert.Equal(
            Fulfillment.Unknown,
            copy.OfferResult(
                offer.Id,
                copy.Criteria.Single(criterion => criterion.Name == "Service").Id
            )
        );
        Assert.Empty(copy.Shares);

        // Files with an empty id or a broken version numbering are rejected.
        foreach (var property in new[] { "candidateId", "id" })
        {
            var invalid = JsonNode.Parse(bytes)!;
            invalid["offers"]![0]![property] = Guid.Empty;

            await Assert.ThrowsAsync<RuleException>(() => ImportAsync(fixture, owner, invalid));
        }

        var malformed = JsonNode.Parse(bytes)!;
        malformed["offers"]![0]!["revisions"]![1]!["number"] = 4;

        await Assert.ThrowsAsync<RuleException>(() => ImportAsync(fixture, owner, malformed));

        // A file from before offers existed imports without offers and without a currency.
        var legacy = JsonNode.Parse(bytes)!;
        legacy.AsObject().Remove("calculationSettings");
        legacy.AsObject().Remove("scenarios");
        legacy["version"] = 3;
        legacy.AsObject().Remove("country");
        foreach (
            var property in new[]
            {
                "currency",
                "offers",
                "offerAssessments",
                "costPlanning",
                "costs",
            }
        )
        {
            legacy.AsObject().Remove(property);
        }

        var legacyId = await ImportAsync(fixture, owner, legacy);
        var old = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(legacyId));
        Assert.Single(old.Candidates);
        Assert.Empty(old.Offers);
        Assert.Null(old.Currency);
    }

    /// <summary>
    /// One car with three offers. The first is assessed, then revised with a full price list whose
    /// benefit line is linked to a criterion; the third is moved in front of the second.
    /// </summary>
    private static void AddOffersWithHistory(Evaluation evaluation)
    {
        var candidate = evaluation.AddCandidate(new("Vehicle"));
        var criterion = evaluation.AddCriterion(
            new("Service", CriterionType.Boolean, Priority.Required, CriterionScope.Offer)
        );
        var wheels = evaluation.AddCriterion(
            new("Wheels", CriterionType.Boolean, Priority.Preferred, CriterionScope.Offer)
        );

        var offer = evaluation.AddOffer(
            candidate,
            new() { Name = "Request" },
            [
                new(
                    "Service",
                    OfferLineKind.Benefit,
                    OfferPriceKind.Included,
                    Effect: OfferBenefitEffect.CostSaving,
                    CostCategory: CostCategory.Service,
                    DurationMonths: 24
                ),
            ],
            "EUR"
        );
        evaluation.AssessOffer(offer, criterion, new(BooleanValue: true, Notes: "Confirmed"));

        evaluation.ReviseOffer(
            offer,
            new() { Name = "Negotiated", StatedTotal = 27800 },
            [
                new("Car", OfferLineKind.Vehicle, OfferPriceKind.Amount, 28000),
                new("Delivery", OfferLineKind.Fee, OfferPriceKind.Amount, 800, Financed: false),
                new("Fleet", OfferLineKind.Discount, OfferPriceKind.Amount, 1000),
                new(
                    "Wheels",
                    OfferLineKind.Benefit,
                    OfferPriceKind.Included,
                    Value: 900,
                    CriterionId: wheels
                ),
            ],
            "Completed"
        );

        evaluation.AddOffer(candidate, new() { Name = "Second" }, []);
        evaluation.MoveOffer(evaluation.AddOffer(candidate, new() { Name = "Third" }, []), -1);
    }

    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Database_rejects_cross_case_offers_and_revision_assessments_and_candidate_deletion_cascades(
        int port
    )
    {
        // Arrange: two separate evaluations, the first with an assessed offer.
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync();
        var first = new Evaluation(owner, "First", null);
        var second = new Evaluation(owner, "Second", null);
        var candidate = first.AddCandidate(new("Vehicle"));
        var foreignCandidate = second.AddCandidate(new("Other"));
        var criterion = first.AddCriterion(
            new("Service", CriterionType.Boolean, Priority.Required, CriterionScope.Offer)
        );
        var foreignCriterion = second.AddCriterion(
            new("Foreign", CriterionType.Boolean, Priority.Required, CriterionScope.Offer)
        );
        var offer = first.AddOffer(candidate, new() { Name = "Request" }, [new("Fee")], "EUR");
        first.AssessOffer(offer, criterion, new(BooleanValue: false));

        await fixture.ExecuteAsync(async provider =>
        {
            var db = provider.GetRequiredService<AppDbContext>();
            db.AddRange(first, second);
            await db.SaveChangesAsync();
        });

        // An offer cannot point at a car of another evaluation.
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            fixture.ExecuteSqlAsync(
                $"INSERT INTO \"Offer\" (\"Id\", \"EvaluationId\", \"CandidateId\", \"Position\") VALUES ({Guid.NewGuid()}, {first.Id}, {foreignCandidate}, 0)"
            )
        );
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);

        // An assessment cannot point at a criterion of another evaluation.
        var assessmentError = await Assert.ThrowsAsync<PostgresException>(() =>
            fixture.ExecuteSqlAsync(
                $"INSERT INTO \"OfferAssessment\" (\"Id\", \"EvaluationId\", \"OfferId\", \"CriterionId\", \"RevisionNumber\", \"Source\", \"Notes\") VALUES ({Guid.NewGuid()}, {first.Id}, {offer}, {foreignCriterion}, 1, 'Unknown', '')"
            )
        );
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, assessmentError.SqlState);

        // Removing the car takes its offers, versions, lines and assessments with it.
        await fixture.ActAsync(
            owner,
            workspace =>
                workspace.EditAsync(
                    first.Id,
                    1,
                    evaluation => evaluation.RemoveCandidate(candidate)
                )
        );

        var saved = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(first.Id));
        Assert.Empty(saved.Candidates);
        Assert.Empty(saved.Offers);
        Assert.Empty(saved.OfferAssessments);
        await fixture.ExecuteAsync(async provider =>
        {
            var db = provider.GetRequiredService<AppDbContext>();
            Assert.Empty(await db.Set<OfferRevision>().ToListAsync());
            Assert.Empty(await db.Set<OfferLine>().ToListAsync());
        });
    }

    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Migration_retains_existing_specifications_and_starts_with_no_offers_or_currency(
        int port
    )
    {
        // Arrange: an evaluation with one car that carries technical data.
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync();
        var evaluationId = await fixture.AsAsync(
            owner,
            workspace => workspace.CreateAsync("Existing", null, "CH", "EUR")
        );
        var vehicle = new CandidateInput(
            "Vehicle",
            Specifications: new()
            {
                PowerKw = 125,
                KerbWeightKg = 1800,
                GrossWeightKg = 2250,
            }
        );
        await fixture.ActAsync(
            owner,
            workspace =>
                workspace.EditAsync(evaluationId, 1, evaluation => evaluation.AddCandidate(vehicle))
        );

        // Act: go back to the schema before offers existed, then forward to the current one.
        await fixture.ExecuteAsync(async provider =>
        {
            var migrator = provider.GetRequiredService<AppDbContext>().GetService<IMigrator>();
            await migrator.MigrateAsync("20261110181930_VehicleSpecifications");
            await migrator.MigrateAsync();
        });

        // Assert
        var saved = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(evaluationId));
        Assert.Equal(450, saved.Candidates.Single().Specifications.GetPayloadKg());
        Assert.Empty(saved.Offers);
        Assert.Null(saved.Currency);
    }

    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Files_from_before_the_price_list_import_with_a_vehicle_line_and_without_the_archive(
        int port
    )
    {
        // Arrange: export an evaluation with one offer.
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync();
        var evaluationId = await fixture.AsAsync(
            owner,
            workspace => workspace.CreateAsync("Decision", null, "CH", "EUR")
        );
        await fixture.ActAsync(
            owner,
            workspace =>
                workspace.EditAsync(
                    evaluationId,
                    1,
                    evaluation =>
                    {
                        var candidate = evaluation.AddCandidate(new("Vehicle"));
                        evaluation.AddOffer(
                            candidate,
                            new() { Name = "Request", VehicleDescription = "Vehicle Long Range" },
                            [new("Mats", OfferLineKind.Accessory, OfferPriceKind.Included)],
                            "EUR"
                        );
                    }
                )
        );
        var content = JsonNode.Parse(
            await fixture.TransferAsync(owner, transfer => transfer.ExportAsync(evaluationId))
        )!;

        // Rewrite the file into the old format: a base price in the terms, an archive flag and
        // lines without the fields that came with the price list.
        content["version"] = 8;
        var offer = content["offers"]![0]!;
        offer["archived"] = true;
        var revision = offer["revisions"]![0]!;
        revision["terms"]!.AsObject().Remove("statedTotal");
        revision["terms"]!["basePrice"] = 41000;
        foreach (var line in revision["lines"]!.AsArray())
        {
            line!.AsObject().Remove("financed");
            line.AsObject().Remove("value");
            line.AsObject().Remove("criterionId");
        }

        // Act
        var copyId = await ImportAsync(fixture, owner, content);

        // Assert: the base price became the leading vehicle line.
        var copy = (await fixture.AsAsync(owner, workspace => workspace.ReadAsync(copyId)))
            .Offers.Single()
            .Latest;
        Assert.Equal(
            ["Vehicle Long Range", "Mats"],
            copy.Lines.OrderBy(line => line.Position).Select(line => line.Name)
        );
        Assert.Equal(41000, copy.Pricing.Total);
        Assert.Null(copy.Terms.StatedTotal);
        Assert.All(copy.Lines, line => Assert.True(line.Financed));

        // A current file must not carry the fields of the old format.
        content["version"] = 9;
        await Assert.ThrowsAsync<RuleException>(() => ImportAsync(fixture, owner, content));
    }

    private static Task<Guid> ImportAsync(PostgresFixture fixture, string actor, JsonNode file)
    {
        return fixture.TransferAsync(
            actor,
            transfer => transfer.ImportAsync(Encoding.UTF8.GetBytes(file.ToJsonString()))
        );
    }
}
