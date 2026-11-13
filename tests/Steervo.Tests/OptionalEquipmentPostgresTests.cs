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

[Collection("Postgres")]
public sealed class OptionalEquipmentPostgresTests
{
    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Migration_preserves_existing_answers_and_rejects_conflicting_values(int port)
    {
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync();
        var id = await fixture.AsAsync(owner, w => w.CreateAsync("Existing answers", null));
        await fixture.AsAsync(owner, async w => { await w.EditAsync(id, 1, e => {
            var candidate = e.AddCandidate(new("Vehicle"));
            var offer = e.AddOffer(candidate, new() { Name = "Offer" }, [], "EUR");
            foreach (var answer in new bool?[] { true, false, null })
            {
                var criterion = e.AddCriterion(new("Feature", CriterionType.Boolean, Priority.Required, CriterionScope.Vehicle));
                e.AssessCandidate(candidate, criterion, new(BooleanValue: answer));
                var offered = e.AddCriterion(new("Offered feature", CriterionType.Boolean, Priority.Required, CriterionScope.Offer));
                e.AssessOffer(offer, offered, new(BooleanValue: answer));
            }
        }); return true; });
        await fixture.RunAsync(async p => {
            var migrator = p.GetRequiredService<AppDbContext>().GetService<IMigrator>();
            await migrator.MigrateAsync("20261110202938_Scenarios");
            await migrator.MigrateAsync(); return true;
        });
        var saved = await fixture.AsAsync(owner, w => w.ReadAsync(id));
        Assert.Equal(new bool?[] { null, false, true }, saved.Assessments.Select(a => a.BooleanValue).Order());
        Assert.Equal(new bool?[] { null, false, true }, saved.OfferAssessments.Select(a => a.BooleanValue).Order());
        Assert.All(saved.Assessments, a => Assert.False(a.OptionalAvailable));
        Assert.All(saved.OfferAssessments, a => Assert.False(a.OptionalAvailable));
        foreach (var sql in new[] {
            "UPDATE \"Assessment\" SET \"OptionalAvailable\" = true WHERE \"BooleanValue\" IS NOT NULL",
            "UPDATE \"OfferAssessment\" SET \"OptionalAvailable\" = true WHERE \"BooleanValue\" IS NOT NULL" })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => fixture.RunAsync(async p => {
                await p.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync(sql); return true;
            }));
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        }
    }

    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Optional_answers_roundtrip_through_json_and_archives_and_legacy_answers_stay_distinct(int port)
    {
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync();
        var id = await fixture.AsAsync(owner, w => w.CreateAsync("Optional equipment", null));
        Guid offerId = default;
        await fixture.AsAsync(owner, async w => { await w.EditAsync(id, 1, e => {
            var candidate = e.AddCandidate(new("Vehicle"));
            var criterion = e.AddCriterion(new("Physical key", CriterionType.Boolean, Priority.Required, CriterionScope.Vehicle));
            var offered = e.AddCriterion(new("Accessory", CriterionType.Boolean, Priority.Required, CriterionScope.Offer));
            offerId = e.AddOffer(candidate, new() { Name = "Offer" }, [], "EUR");
            e.AssessCandidate(candidate, criterion, new(OptionalAvailable: true, Notes: "Separate purchase"));
            e.AssessOffer(offerId, offered, new(OptionalAvailable: true, Source: AssessmentSource.Manufacturer));
        }); return true; });
        var bytes = await fixture.TransferAsync(owner, t => t.ExportAsync(id));
        var json = JsonNode.Parse(bytes)!;
        Assert.Equal(7, json["version"]!.GetValue<int>());
        var copyId = await fixture.TransferAsync(owner, t => t.ImportAsync(bytes));
        var copy = await fixture.AsAsync(owner, w => w.ReadAsync(copyId));
        AssertOptional(copy);
        Assert.NotEqual(offerId, copy.Offers.Single().Id);

        // Old formats cannot label a newly introduced optional answer as an old boolean.
        json["version"] = 6;
        await Assert.ThrowsAsync<RuleException>(() => fixture.TransferAsync(owner, t => t.ImportAsync(Encoding.UTF8.GetBytes(json.ToJsonString()))));
        json["assessments"]![0]!.AsObject().Remove("optionalAvailable");
        json["offerAssessments"]![0]!.AsObject().Remove("optionalAvailable");
        json["assessments"]![0]!["booleanValue"] = true;
        json["offerAssessments"]![0]!["booleanValue"] = false;
        var legacyId = await fixture.TransferAsync(owner, t => t.ImportAsync(Encoding.UTF8.GetBytes(json.ToJsonString())));
        var legacy = await fixture.AsAsync(owner, w => w.ReadAsync(legacyId));
        Assert.True(legacy.Assessments.Single().BooleanValue); Assert.False(legacy.Assessments.Single().OptionalAvailable);
        Assert.False(legacy.OfferAssessments.Single().BooleanValue); Assert.False(legacy.OfferAssessments.Single().OptionalAvailable);

        var invalid = JsonNode.Parse(bytes)!;
        invalid["assessments"]![0]!["booleanValue"] = true;
        await Assert.ThrowsAsync<RuleException>(() => fixture.TransferAsync(owner, t => t.ImportAsync(Encoding.UTF8.GetBytes(invalid.ToJsonString()))));
        Assert.Equal(3, (await fixture.AsAsync(owner, w => w.ListAsync())).Count);

        await fixture.DocumentsAsync(owner, async w => {
            using var content = new MemoryStream(Encoding.UTF8.GetBytes("Accessory details"));
            await w.UploadAsync(id, 2, offerId, null, "Accessory", content, "accessory.txt", null); return true;
        });
        var export = await fixture.ArchiveAsync(owner, a => a.ExportAsync(id));
        await using var archive = export.Content;
        Assert.Equal("application/zip", export.ContentType);
        var archiveId = await fixture.ArchiveAsync(owner, a => a.ImportAsync(archive));
        var archived = await fixture.AsAsync(owner, w => w.ReadAsync(archiveId));
        AssertOptional(archived); Assert.Single(archived.Documents);
    }

    private static void AssertOptional(Evaluation evaluation)
    {
        var vehicle = Assert.Single(evaluation.Assessments);
        var offer = Assert.Single(evaluation.OfferAssessments);
        Assert.True(vehicle.OptionalAvailable); Assert.Null(vehicle.BooleanValue);
        Assert.Equal("Separate purchase", vehicle.Notes);
        Assert.True(offer.OptionalAvailable); Assert.Null(offer.BooleanValue);
        Assert.Equal(Fulfillment.Optional, evaluation.ResultFor(vehicle.CandidateId, vehicle.CriterionId));
        Assert.Equal(Fulfillment.Optional, evaluation.OfferResult(offer.OfferId, offer.CriterionId));
    }
}
