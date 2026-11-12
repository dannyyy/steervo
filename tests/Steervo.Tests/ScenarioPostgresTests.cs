using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Steervo.Application.Evaluations;
using Steervo.Application.Scenarios;
using Steervo.Calculations;
using Steervo.Core;
using Steervo.Infrastructure.Data;
using Xunit;

namespace Steervo.Tests;

[Collection("Postgres")]
public sealed class ScenarioPostgresTests
{
    private static ScenarioTerms Terms => new() { Mode = AcquisitionMode.Purchase, PurchasePrice = 1500, InitialFees = 0, CostsReviewed = true };
    private static Evaluation Seed(string owner)
    {
        var e = new Evaluation(owner, "Scenario case", null); var car = e.AddCandidate(new("Vehicle"));
        e.AddOffer(car, new() { Name = "Source", BasePrice = 1700 }, [], "EUR"); e.SetCostPlanning(new(2027, 10, 1), 24);
        e.AddCost(new("Shared", CostCategory.Parking, CostFrequency.Monthly, 20)); return e;
    }
    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Scenario_mutations_preserve_pinned_versions_enforce_roles_and_cascade_safely(int port)
    {
        await using var f = await PostgresFixture.CreateAsync(port); var owner = await f.SetupAsync();
        var reader = await f.InviteAsync(owner, "reader@example.test"); var editor = await f.InviteAsync(owner, "editor@example.test");
        var e = Seed(owner); var offer = e.Offers.Single().Id; var car = e.Candidates.Single().Id;
        await f.RunAsync(async p => { await p.GetRequiredService<IEvaluationStore>().CreateAsync(e); return true; });
        await f.AsAsync(owner, async w => { await w.ShareAsync(e.Id, 1, "reader@example.test", AccessLevel.Reader); await w.ShareAsync(e.Id, 2, "editor@example.test", AccessLevel.Editor); return true; });
        await Assert.ThrowsAsync<RuleException>(() => f.AsAsync(reader, w => new ScenarioWorkspace(w).AddAsync(e.Id, 3, new("Denied", offer, 1, Terms))));
        await f.AsAsync(editor, async w => { await new ScenarioWorkspace(w).SettingsAsync(e.Id, 3, 0); return true; });
        var scenarioId = await f.AsAsync(editor, w => new ScenarioWorkspace(w).AddAsync(e.Id, 4, new("Cash", offer, 1, Terms)));
        await f.AsAsync(editor, async w => { await new ScenarioWorkspace(w).ResidualAsync(e.Id, 5, scenarioId, new(12, 500, 600, 700)); return true; });
        await f.AsAsync(editor, async w => { await new ScenarioWorkspace(w).AdjustmentAsync(e.Id, 6, scenarioId, new(e.Costs.Single().Id, 0, 1, 6)); return true; });
        var saved = await f.AsAsync(reader, w => w.ReadAsync(e.Id)); Assert.Equal(1020, ScenarioCalculation.Calculate(saved, scenarioId, 12).ExpectedCost);
        await Assert.ThrowsAsync<RuleException>(() => f.AsAsync(editor, async w => { await new ScenarioWorkspace(w).ResidualAsync(e.Id, 5, scenarioId, new(12, 0, 0, 0)); return true; }));
        await Assert.ThrowsAsync<RuleException>(() => f.AsAsync(reader, async w => { await new ScenarioWorkspace(w).RemoveAsync(e.Id, 7, scenarioId); return true; }));
        var foreign = Seed(owner);
        await f.RunAsync(async p => { await p.GetRequiredService<IEvaluationStore>().CreateAsync(foreign); return true; });
        var error = await Assert.ThrowsAsync<PostgresException>(() => f.RunAsync(async p => { await p.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Scenario\" SET \"OfferId\" = {foreign.Offers.Single().Id} WHERE \"Id\" = {scenarioId}"); return true; }));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
        await Assert.ThrowsAsync<RuleException>(() => f.AsAsync(editor, async w => { await new ScenarioWorkspace(w).AdjustmentAsync(e.Id, 7, scenarioId, new(foreign.Costs.Single().Id, 0)); return true; }));
        await f.AsAsync(owner, async w => { await w.EditAsync(e.Id, 7, x => x.ReviseOffer(offer, new() { Name = "Revised", BasePrice = 2000 }, [])); return true; });
        saved = await f.AsAsync(reader, w => w.ReadAsync(e.Id)); Assert.Equal(1, saved.FindScenario(scenarioId).RevisionNumber); Assert.Equal(1020, ScenarioCalculation.Calculate(saved, scenarioId, 12).ExpectedCost);
        await f.AsAsync(owner, async w => { await w.RevokeAsync(e.Id, 8, editor); return true; });
        await Assert.ThrowsAsync<RuleException>(() => f.AsAsync(editor, w => w.ReadAsync(e.Id)));
        await f.AsAsync(owner, async w => { await w.EditAsync(e.Id, 9, x => x.RemoveCandidate(car)); return true; });
        saved = await f.AsAsync(owner, w => w.ReadAsync(e.Id)); Assert.Empty(saved.Scenarios); Assert.Single(saved.Costs);
        await f.RunAsync(async p => { var db = p.GetRequiredService<AppDbContext>(); Assert.Empty(await db.Set<ScenarioCostAdjustment>().ToArrayAsync()); Assert.Empty(await db.Set<ResidualCheckpoint>().ToArrayAsync()); return true; });
    }
    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Scenario_transfer_remaps_sources_and_costs_and_preserves_incomplete_assumptions(int port)
    {
        await using var f = await PostgresFixture.CreateAsync(port); var owner = await f.SetupAsync(); var e = Seed(owner);
        e.SetCalculationSettings(3.25m); var offer = e.Offers.Single().Id;
        var scenario = e.AddScenario(new("Cash", offer, 1, Terms, "Independent assumptions")); e.SetResidual(scenario, new(12, null, 600, 700, "Source note"));
        e.SetCostAdjustment(scenario, new(e.Costs.Single().Id, 0, 1, 4, "Included period"));
        var copyScenario = e.CopyScenario(scenario, "Unknown price"); e.EditScenario(copyScenario, e.FindScenario(copyScenario).ToInput() with { Terms = Terms with { PurchasePrice = null } });
        await f.RunAsync(async p => { await p.GetRequiredService<IEvaluationStore>().CreateAsync(e); return true; });
        var bytes = await f.TransferAsync(owner, t => t.ExportAsync(e.Id)); Assert.Equal(6, JsonNode.Parse(bytes)!["version"]!.GetValue<int>());
        var copied = await f.TransferAsync(owner, t => t.ImportAsync(bytes)); var imported = await f.AsAsync(owner, w => w.ReadAsync(copied));
        Assert.Equal(3.25m, imported.CalculationSettings!.AnnualReturnPercent); Assert.Equal(2, imported.Scenarios.Count); Assert.Empty(imported.Shares);
        foreach (var item in imported.Scenarios)
        {
            Assert.NotEqual(offer, item.OfferId); Assert.DoesNotContain(e.Scenarios, original => original.Id == item.Id);
            Assert.Equal(imported.Costs.Single().Id, item.CostAdjustments.Single().CostId); Assert.Null(item.Residuals.Single().Low);
        }
        Assert.Null(imported.Scenarios.Single(s => s.Name == "Unknown price").Terms.PurchasePrice);
        var actual = imported.Scenarios.Single(s => s.Name == "Cash");
        Assert.Equal(ScenarioCalculation.Calculate(e, scenario, 12).ExpectedCost, ScenarioCalculation.Calculate(imported, actual.Id, 12).ExpectedCost);
        foreach (var invalid in new[] { "source", "cost", "band", "duplicate", "mode" })
        {
            var json = JsonNode.Parse(bytes)!; var input = json["scenarios"]![0]!;
            switch (invalid)
            {
                case "source": input["input"]!["offerId"] = Guid.NewGuid(); break;
                case "cost": input["costAdjustments"]![0]!["costId"] = Guid.NewGuid(); break;
                case "band": input["residuals"]![0]!["low"] = 800; break;
                case "duplicate": input["residuals"]!.AsArray().Add(input["residuals"]![0]!.DeepClone()); break;
                case "mode": input["input"]!["terms"]!["mode"] = "Unrecognized"; break;
            }
            await Assert.ThrowsAsync<RuleException>(() => f.TransferAsync(owner, t => t.ImportAsync(Encoding.UTF8.GetBytes(json.ToJsonString()))));
        }
        var legacy = JsonNode.Parse(bytes)!; legacy["version"] = 5; legacy.AsObject().Remove("calculationSettings"); legacy.AsObject().Remove("scenarios");
        var legacyId = await f.TransferAsync(owner, t => t.ImportAsync(Encoding.UTF8.GetBytes(legacy.ToJsonString())));
        var old = await f.AsAsync(owner, w => w.ReadAsync(legacyId)); Assert.Empty(old.Scenarios); Assert.Null(old.CalculationSettings); Assert.Single(old.Costs);
    }
    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Upgrade_preserves_existing_costs_and_database_rejects_invalid_bands(int port)
    {
        await using var f = await PostgresFixture.CreateAsync(port); var owner = await f.SetupAsync(); var e = Seed(owner);
        await f.RunAsync(async p => { var db = p.GetRequiredService<AppDbContext>(); await db.GetService<IMigrator>().MigrateAsync("20261110195608_CostPlanning"); db.Evaluations.Add(e); await db.SaveChangesAsync(); await db.Database.MigrateAsync(); return true; });
        var saved = await f.AsAsync(owner, w => w.ReadAsync(e.Id)); Assert.Empty(saved.Scenarios); Assert.Null(saved.CalculationSettings); Assert.Equal(20, saved.Costs.Single().Amount);
        Guid scenario = default;
        await f.AsAsync(owner, async w => { await w.EditAsync(e.Id, 1, x => { scenario = x.AddScenario(new("Cash", x.Offers.Single().Id, 1, Terms)); x.SetResidual(scenario, new(12, 500, 600, 700)); }); return true; });
        var error = await Assert.ThrowsAsync<PostgresException>(() => f.RunAsync(async p => { await p.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ResidualCheckpoint\" SET \"Low\" = 999 WHERE \"ScenarioId\" = {scenario}"); return true; }));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }
}
