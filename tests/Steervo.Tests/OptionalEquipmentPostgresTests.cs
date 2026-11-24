using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Steervo.Core;
using Steervo.Infrastructure.Data;
using Xunit;

namespace Steervo.Tests;

/// <summary>
/// Checks that the answer "available as an option" is migrated, stored and transferred correctly
/// on PostgreSQL.
/// </summary>
[Collection("Postgres")]
public sealed class OptionalEquipmentPostgresTests
{
    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Migration_preserves_existing_answers_and_rejects_conflicting_values(int port)
    {
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync();
        var id = await fixture.AsAsync(
            owner,
            workspace => workspace.CreateAsync("Existing answers", null, "CH", "EUR")
        );
        await fixture.AsAsync(
            owner,
            async workspace =>
            {
                await workspace.EditAsync(id, 1, AddYesNoAndUnknownAnswers);
                return true;
            }
        );

        // Go back to the schema before optional answers and upgrade again.
        await fixture.RunAsync(async services =>
        {
            var migrator = services.GetRequiredService<AppDbContext>().GetService<IMigrator>();
            await migrator.MigrateAsync("20261110202938_Scenarios");
            await migrator.MigrateAsync();
            return true;
        });

        var saved = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(id));
        Assert.Equal(
            new bool?[] { null, false, true },
            saved.Assessments.Select(a => a.BooleanValue).Order()
        );
        Assert.Equal(
            new bool?[] { null, false, true },
            saved.OfferAssessments.Select(a => a.BooleanValue).Order()
        );
        Assert.All(saved.Assessments, a => Assert.False(a.OptionalAvailable));
        Assert.All(saved.OfferAssessments, a => Assert.False(a.OptionalAvailable));

        // The database itself refuses an answer that is both yes/no and optional.
        var conflictingUpdates = new[]
        {
            "UPDATE \"Assessment\" SET \"OptionalAvailable\" = true WHERE \"BooleanValue\" IS NOT NULL",
            "UPDATE \"OfferAssessment\" SET \"OptionalAvailable\" = true WHERE \"BooleanValue\" IS NOT NULL",
        };

        foreach (var sql in conflictingUpdates)
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() =>
                fixture.RunAsync(async services =>
                {
                    await services
                        .GetRequiredService<AppDbContext>()
                        .Database.ExecuteSqlRawAsync(sql);
                    return true;
                })
            );
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        }
    }

    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Optional_answers_roundtrip_through_json_and_archives_and_legacy_answers_stay_distinct(
        int port
    )
    {
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync();
        var id = await fixture.AsAsync(
            owner,
            workspace => workspace.CreateAsync("Optional equipment", null, "CH", "EUR")
        );
        Guid offerId = default;
        await fixture.AsAsync(
            owner,
            async workspace =>
            {
                await workspace.EditAsync(
                    id,
                    1,
                    evaluation =>
                    {
                        var candidate = evaluation.AddCandidate(new("Vehicle"));
                        var criterion = evaluation.AddCriterion(
                            new(
                                "Physical key",
                                CriterionType.Boolean,
                                Priority.Required,
                                CriterionScope.Vehicle
                            )
                        );
                        var offerCriterion = evaluation.AddCriterion(
                            new(
                                "Accessory",
                                CriterionType.Boolean,
                                Priority.Required,
                                CriterionScope.Offer
                            )
                        );
                        offerId = evaluation.AddOffer(
                            candidate,
                            new() { Name = "Offer" },
                            [],
                            "EUR"
                        );
                        evaluation.AssessCandidate(
                            candidate,
                            criterion,
                            new(OptionalAvailable: true, Notes: "Separate purchase")
                        );
                        evaluation.AssessOffer(
                            offerId,
                            offerCriterion,
                            new(OptionalAvailable: true, Source: AssessmentSource.Manufacturer)
                        );
                    }
                );
                return true;
            }
        );

        // The current format keeps optional answers.
        var exported = await fixture.TransferAsync(owner, transfer => transfer.ExportAsync(id));
        var json = JsonNode.Parse(exported)!;
        Assert.Equal(9, json["version"]!.GetValue<int>());
        var copyId = await fixture.TransferAsync(owner, transfer => transfer.ImportAsync(exported));
        var copy = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(copyId));
        AssertOptional(copy);
        Assert.NotEqual(offerId, copy.Offers.Single().Id);

        // Old formats cannot label a newly introduced optional answer as an old boolean.
        json["version"] = 6;
        json.AsObject().Remove("country");
        await Assert.ThrowsAsync<RuleException>(() => ImportAsync(fixture, owner, json));

        // A genuine old file carries plain yes/no answers, which stay yes/no.
        json["assessments"]![0]!.AsObject().Remove("optionalAvailable");
        json["offerAssessments"]![0]!.AsObject().Remove("optionalAvailable");
        json["assessments"]![0]!["booleanValue"] = true;
        json["offerAssessments"]![0]!["booleanValue"] = false;
        var legacyId = await ImportAsync(fixture, owner, json);
        var legacy = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(legacyId));
        Assert.True(legacy.Assessments.Single().BooleanValue);
        Assert.False(legacy.Assessments.Single().OptionalAvailable);
        Assert.False(legacy.OfferAssessments.Single().BooleanValue);
        Assert.False(legacy.OfferAssessments.Single().OptionalAvailable);

        // A current file with an answer that is both yes and optional is rejected.
        var invalid = JsonNode.Parse(exported)!;
        invalid["assessments"]![0]!["booleanValue"] = true;
        await Assert.ThrowsAsync<RuleException>(() => ImportAsync(fixture, owner, invalid));
        var evaluations = await fixture.AsAsync(owner, workspace => workspace.ListAsync());
        Assert.Equal(3, evaluations.Count);

        // The archive with documents keeps optional answers as well.
        await fixture.DocumentsAsync(
            owner,
            async documents =>
            {
                using var content = new MemoryStream(Encoding.UTF8.GetBytes("Accessory details"));
                await documents.UploadAsync(
                    id,
                    2,
                    offerId,
                    null,
                    "Accessory",
                    content,
                    "accessory.txt",
                    null
                );
                return true;
            }
        );
        var export = await fixture.ArchiveAsync(owner, archives => archives.ExportAsync(id));
        await using var archive = export.Content;
        Assert.Equal("application/zip", export.ContentType);
        var archiveId = await fixture.ArchiveAsync(
            owner,
            archives => archives.ImportAsync(archive)
        );
        var archived = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(archiveId));
        AssertOptional(archived);
        Assert.Single(archived.Documents);
    }

    /// <summary>
    /// Adds a car and an offer with one boolean criterion each per possible answer.
    /// </summary>
    private static void AddYesNoAndUnknownAnswers(Evaluation evaluation)
    {
        var candidate = evaluation.AddCandidate(new("Vehicle"));
        var offer = evaluation.AddOffer(candidate, new() { Name = "Offer" }, [], "EUR");

        foreach (var answer in new bool?[] { true, false, null })
        {
            var criterion = evaluation.AddCriterion(
                new("Feature", CriterionType.Boolean, Priority.Required, CriterionScope.Vehicle)
            );
            evaluation.AssessCandidate(candidate, criterion, new(BooleanValue: answer));

            var offerCriterion = evaluation.AddCriterion(
                new(
                    "Offered feature",
                    CriterionType.Boolean,
                    Priority.Required,
                    CriterionScope.Offer
                )
            );
            evaluation.AssessOffer(offer, offerCriterion, new(BooleanValue: answer));
        }
    }

    private static Task<Guid> ImportAsync(PostgresFixture fixture, string owner, JsonNode file)
    {
        return fixture.TransferAsync(
            owner,
            transfer => transfer.ImportAsync(Encoding.UTF8.GetBytes(file.ToJsonString()))
        );
    }

    private static void AssertOptional(Evaluation evaluation)
    {
        var vehicle = Assert.Single(evaluation.Assessments);
        var offer = Assert.Single(evaluation.OfferAssessments);

        Assert.True(vehicle.OptionalAvailable);
        Assert.Null(vehicle.BooleanValue);
        Assert.Equal("Separate purchase", vehicle.Notes);
        Assert.True(offer.OptionalAvailable);
        Assert.Null(offer.BooleanValue);
        Assert.Equal(
            Fulfillment.Optional,
            evaluation.ResultFor(vehicle.CandidateId, vehicle.CriterionId)
        );
        Assert.Equal(
            Fulfillment.Optional,
            evaluation.OfferResult(offer.OfferId, offer.CriterionId)
        );
    }
}
