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

[Collection("Postgres")]
public sealed class VehicleSpecificationsPostgresTests
{
    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Specifications_survive_reload_copy_export_and_import_including_their_calculation_sources(int port)
    {
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync();
        var id = await fixture.AsAsync(owner, w => w.CreateAsync("Decision", null));
        var specifications = new VehicleSpecifications
        {
            Propulsion = PropulsionType.Hybrid, Drive = DrivenWheels.All,
            LengthMm = 4700, WidthMm = 1900, WidthWithMirrorsMm = 2100, HeightMm = 1600, WheelbaseMm = 2850,
            Seats = 5, BootVolumeLitres = 500, BootVolumeFoldedLitres = 1600, FrunkVolumeLitres = 0,
            KerbWeightKg = 2000, GrossWeightKg = 2500, PowerPs = 400, AccelerationSeconds = 4.5m, TopSpeedKmh = 220,
            SummerConsumptionKwhPer100Km = 16.5m, WinterConsumptionKwhPer100Km = 22.4m,
            BatteryUsableKwh = 75, BatteryGrossKwh = 80, AcChargingPowerKw = 11, DcChargingPowerKw = 250, Charging20To80Minutes = 22.5m,
            TankCapacityLitres = 50, RangeKm = 800, FuelConsumptionLitresPer100Km = 6.25m
        };
        await fixture.AsAsync(owner, async w => { await w.EditAsync(id, 1, e => e.AddCandidate(new("Original", Specifications: specifications))); return true; });
        var saved = await fixture.AsAsync(owner, w => w.ReadAsync(id));
        Assert.Equal(specifications, saved.Candidates.Single().Specifications);
        await fixture.AsAsync(owner, async w => { await w.EditAsync(id, saved.Version, e => e.CopyCandidate(saved.Candidates.Single().Id, "Copy")); return true; });
        var bytes = await fixture.TransferAsync(owner, t => t.ExportAsync(id));
        Assert.Equal(6, JsonNode.Parse(bytes)!["version"]!.GetValue<int>());
        var copyId = await fixture.TransferAsync(owner, t => t.ImportAsync(bytes));
        var imported = await fixture.AsAsync(owner, w => w.ReadAsync(copyId));
        Assert.All(imported.Candidates, candidate => Assert.Equal(specifications, candidate.Specifications));
        Assert.All(imported.Candidates, candidate => Assert.Null(candidate.Specifications.PayloadKg));
        Assert.All(imported.Candidates, candidate => Assert.Equal(500, candidate.Specifications.GetPayloadKg()));
        Assert.Empty(imported.Assessments);
        var legacy = JsonNode.Parse(bytes)!; legacy.AsObject().Remove("calculationSettings"); legacy.AsObject().Remove("scenarios");
        legacy["version"] = 2;
        foreach (var property in new[] { "currency", "offers", "offerAssessments", "costPlanning", "costs" }) legacy.AsObject().Remove(property);
        foreach (var candidate in legacy["candidates"]!.AsArray()) candidate!.AsObject().Remove("specifications");
        var legacyId = await fixture.TransferAsync(owner, t => t.ImportAsync(Encoding.UTF8.GetBytes(legacy.ToJsonString())));
        Assert.All((await fixture.AsAsync(owner, w => w.ReadAsync(legacyId))).Candidates, candidate => Assert.False(candidate.Specifications.HasValues()));
        var invalid = JsonNode.Parse(bytes)!;
        invalid["candidates"]![0]!["specifications"]!["grossWeightKg"] = 1;
        await Assert.ThrowsAsync<RuleException>(() => fixture.TransferAsync(owner, t => t.ImportAsync(Encoding.UTF8.GetBytes(invalid.ToJsonString()))));
    }

    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Additive_migration_preserves_existing_candidates_and_assessments_with_unknown_specifications(int port)
    {
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync();
        var id = await fixture.AsAsync(owner, w => w.CreateAsync("Existing decision", null));
        Guid candidateId = default, criterionId = default;
        await fixture.AsAsync(owner, async w => { await w.EditAsync(id, 1, e => {
            candidateId = e.AddCandidate(new("Existing vehicle", Notes: "Keep this note"));
            criterionId = e.AddCriterion(new("Impression", CriterionType.Rating, Priority.Required, CriterionScope.Vehicle));
            e.AssessCandidate(candidateId, criterionId, new(RatingValue: 1));
        }); return true; });
        await fixture.RunAsync(async p => {
            var migrator = p.GetRequiredService<AppDbContext>().GetService<IMigrator>();
            await migrator.MigrateAsync("20261109203806_CandidatesAndAssessments");
            await migrator.MigrateAsync();
            return true;
        });
        var saved = await fixture.AsAsync(owner, w => w.ReadAsync(id));
        Assert.Equal(candidateId, saved.Candidates.Single().Id);
        Assert.Equal("Keep this note", saved.Candidates.Single().Notes);
        Assert.False(saved.Candidates.Single().Specifications.HasValues());
        Assert.Equal(Fulfillment.Rated, saved.ResultFor(candidateId, criterionId));
        Assert.Equal(1, saved.Assessments.Single().RatingValue);
    }
}
