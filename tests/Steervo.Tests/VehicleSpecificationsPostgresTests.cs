using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Steervo.Core;
using Steervo.Infrastructure.Data;
using Xunit;

namespace Steervo.Tests;

/// <summary>
/// Checks that the technical data of a car is stored, transferred and migrated correctly on
/// PostgreSQL.
/// </summary>
[Collection("Postgres")]
public sealed class VehicleSpecificationsPostgresTests
{
    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Specifications_survive_reload_copy_export_and_import_including_their_calculation_sources(
        int port
    )
    {
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync();
        var id = await fixture.AsAsync(owner, workspace =>
            workspace.CreateAsync("Decision", null, "CH", "EUR")
        );
        var specifications = CreateFullSpecifications();
        await EditAsync(fixture, owner, id, 1, evaluation =>
            evaluation.AddCandidate(new("Original", Specifications: specifications))
        );

        // Every value survives saving and reloading.
        var saved = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(id));
        Assert.Equal(specifications, saved.Candidates.Single().Specifications);

        // The copied car and the exported file carry the same data. The payload was never
        // entered, so it stays empty and is still derived from the two weights.
        await EditAsync(fixture, owner, id, saved.Version, evaluation =>
            evaluation.CopyCandidate(saved.Candidates.Single().Id, "Copy")
        );
        var exported = await fixture.TransferAsync(owner, transfer => transfer.ExportAsync(id));
        Assert.Equal(9, JsonNode.Parse(exported)!["version"]!.GetValue<int>());
        var copyId = await fixture.TransferAsync(owner, transfer => transfer.ImportAsync(exported));
        var imported = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(copyId));
        Assert.All(imported.Candidates, candidate =>
            Assert.Equal(specifications, candidate.Specifications)
        );
        Assert.All(imported.Candidates, candidate =>
            Assert.Null(candidate.Specifications.PayloadKg)
        );
        Assert.All(imported.Candidates, candidate =>
            Assert.Equal(500, candidate.Specifications.GetPayloadKg())
        );
        Assert.Empty(imported.Assessments);

        // A file of format version 2 has no technical data; its cars are imported without any.
        var legacy = JsonNode.Parse(exported)!;
        legacy.AsObject().Remove("calculationSettings");
        legacy.AsObject().Remove("scenarios");
        legacy["version"] = 2;
        legacy.AsObject().Remove("country");
        var propertiesUnknownToVersion2 = new[]
        {
            "currency",
            "offers",
            "offerAssessments",
            "costPlanning",
            "costs",
        };

        foreach (var property in propertiesUnknownToVersion2)
        {
            legacy.AsObject().Remove(property);
        }

        foreach (var candidate in legacy["candidates"]!.AsArray())
        {
            candidate!.AsObject().Remove("specifications");
        }

        var legacyId = await ImportAsync(fixture, owner, legacy);
        var importedLegacy = await fixture.AsAsync(owner, workspace =>
            workspace.ReadAsync(legacyId)
        );
        Assert.All(importedLegacy.Candidates, candidate =>
            Assert.False(candidate.Specifications.HasValues())
        );

        // Contradictory technical data makes a file invalid.
        var invalid = JsonNode.Parse(exported)!;
        invalid["candidates"]![0]!["specifications"]!["grossWeightKg"] = 1;
        await Assert.ThrowsAsync<RuleException>(() => ImportAsync(fixture, owner, invalid));
    }

    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Additive_migration_preserves_existing_candidates_and_assessments_with_unknown_specifications(
        int port
    )
    {
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync();
        var id = await fixture.AsAsync(owner, workspace =>
            workspace.CreateAsync("Existing decision", null, "CH", "EUR")
        );
        Guid candidateId = default;
        Guid criterionId = default;
        await EditAsync(fixture, owner, id, 1, evaluation =>
        {
            candidateId = evaluation.AddCandidate(new("Existing vehicle", Notes: "Keep this note"));
            criterionId = evaluation.AddCriterion(
                new("Impression", CriterionType.Rating, Priority.Required, CriterionScope.Vehicle)
            );
            evaluation.AssessCandidate(candidateId, criterionId, new(RatingValue: 1));
        });

        // Go back to the schema before technical data and upgrade again.
        await fixture.RunAsync(async services =>
        {
            var migrator = services.GetRequiredService<AppDbContext>().GetService<IMigrator>();
            await migrator.MigrateAsync("20261109203806_CandidatesAndAssessments");
            await migrator.MigrateAsync();
            return true;
        });

        var saved = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(id));

        Assert.Equal(candidateId, saved.Candidates.Single().Id);
        Assert.Equal("Keep this note", saved.Candidates.Single().Notes);
        Assert.False(saved.Candidates.Single().Specifications.HasValues());
        Assert.Equal(Fulfillment.Rated, saved.ResultFor(candidateId, criterionId));
        Assert.Equal(1, saved.Assessments.Single().RatingValue);
    }

    private static VehicleSpecifications CreateFullSpecifications()
    {
        return new VehicleSpecifications
        {
            Propulsion = PropulsionType.Hybrid,
            Drive = DrivenWheels.All,
            LengthMm = 4700,
            WidthMm = 1900,
            WidthWithMirrorsMm = 2100,
            HeightMm = 1600,
            WheelbaseMm = 2850,
            Seats = 5,
            BootVolumeLitres = 500,
            BootVolumeFoldedLitres = 1600,
            FrunkVolumeLitres = 0,
            KerbWeightKg = 2000,
            GrossWeightKg = 2500,
            PowerPs = 400,
            AccelerationSeconds = 4.5m,
            TopSpeedKmh = 220,
            SummerConsumptionKwhPer100Km = 16.5m,
            WinterConsumptionKwhPer100Km = 22.4m,
            BatteryUsableKwh = 75,
            BatteryGrossKwh = 80,
            AcChargingPowerKw = 11,
            DcChargingPowerKw = 250,
            Charging20To80Minutes = 22.5m,
            TankCapacityLitres = 50,
            RangeKm = 800,
            FuelConsumptionLitresPer100Km = 6.25m,
        };
    }

    private static Task<bool> EditAsync(
        PostgresFixture fixture,
        string actor,
        Guid evaluationId,
        long version,
        Action<Evaluation> change
    )
    {
        return fixture.AsAsync(actor, async workspace =>
        {
            await workspace.EditAsync(evaluationId, version, change);
            return true;
        });
    }

    private static Task<Guid> ImportAsync(PostgresFixture fixture, string owner, JsonNode file)
    {
        return fixture.TransferAsync(owner, transfer =>
            transfer.ImportAsync(Encoding.UTF8.GetBytes(file.ToJsonString()))
        );
    }
}
