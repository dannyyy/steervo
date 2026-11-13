using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Steervo.Application.Costs;
using Steervo.Application.Evaluations;
using Steervo.Calculations;
using Steervo.Core;
using Steervo.Infrastructure.Data;
using Xunit;

namespace Steervo.Tests;

[Collection("Postgres")]
public sealed class CostPostgresTests
{
    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Costs_enforce_sharing_conflicts_and_cross_case_references(int port)
    {
        await using var f = await PostgresFixture.CreateAsync(port); var owner = await f.SetupAsync();
        var reader = await f.InviteAsync(owner, "reader@example.test"); var editor = await f.InviteAsync(owner, "editor@example.test");
        var id = await f.AsAsync(owner, w => w.CreateAsync("Planning", null)); Guid car = default;
        await f.AsAsync(owner, async w => { await w.EditAsync(id, 1, e => { car = e.AddCandidate(new("Vehicle")); e.AddOffer(car, new() { Name = "Offer" }, [], "EUR"); }); return true; });
        await f.AsAsync(owner, async w => { await w.ShareAsync(id, 2, "reader@example.test", AccessLevel.Reader); await w.ShareAsync(id, 3, "editor@example.test", AccessLevel.Editor); return true; });
        await Assert.ThrowsAsync<RuleException>(() => f.AsAsync(reader, async w => { await new CostWorkspace(w).PlanAsync(id, 4, new(2027, 1, 1), 24); return true; }));
        await f.AsAsync(editor, async w => { await new CostWorkspace(w).PlanAsync(id, 4, new(2027, 1, 1), 24); return true; });
        var cost = await f.AsAsync(editor, w => new CostWorkspace(w).AddAsync(id, 5, new("Cover", CostCategory.Insurance, CostFrequency.Yearly, 180, CandidateId: car)));
        var saved = await f.AsAsync(reader, w => w.ReadAsync(id)); Assert.Equal(360, CostProjection.Calculate(saved, car).Totals.Net);
        await Assert.ThrowsAsync<RuleException>(() => f.AsAsync(reader, async w => { await new CostWorkspace(w).RemoveAsync(id, 6, cost); return true; }));
        await Assert.ThrowsAsync<RuleException>(() => f.AsAsync(editor, async w => { await new CostWorkspace(w).EditAsync(id, 5, cost, saved.FindCost(cost).ToInput() with { Amount = 1 }); return true; }));
        var other = new Evaluation(owner, "Other", null); var foreignCar = other.AddCandidate(new("Other car"));
        await f.RunAsync(async p => { await p.GetRequiredService<IEvaluationStore>().CreateAsync(other); return true; });
        await Assert.ThrowsAsync<RuleException>(() => f.AsAsync(editor, w => new CostWorkspace(w).AddAsync(id, 6, saved.FindCost(cost).ToInput() with { CandidateId = foreignCar })));
        var error = await Assert.ThrowsAsync<PostgresException>(() => f.RunAsync(async p => { await p.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync($"UPDATE \"CostItem\" SET \"CandidateId\" = {foreignCar} WHERE \"Id\" = {cost}"); return true; }));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
        await f.AsAsync(owner, async w => { await w.RevokeAsync(id, 6, editor); return true; });
        await Assert.ThrowsAsync<RuleException>(() => f.AsAsync(editor, w => w.ReadAsync(id)));
        await f.AsAsync(owner, async w => { await w.EditAsync(id, 7, e => e.RemoveCandidate(car)); return true; });
        Assert.Empty((await f.AsAsync(owner, w => w.ReadAsync(id))).Costs);
    }
    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Transfer_preserves_unknown_included_timing_and_independent_candidate_costs(int port)
    {
        await using var f = await PostgresFixture.CreateAsync(port); var owner = await f.SetupAsync();
        var id = await f.AsAsync(owner, w => w.CreateAsync("Planning", null));
        await f.AsAsync(owner, async w => { await w.EditAsync(id, 1, e => {
            var car = e.AddCandidate(new("Vehicle")); e.SetCurrency("EUR"); e.SetCostPlanning(new(2027, 10, 1), 18);
            e.AddCost(new("Shared", CostCategory.Parking, CostFrequency.Monthly, 25));
            e.AddCost(new("Unknown", CostCategory.Insurance, CostFrequency.Yearly, null, 2, 15, car));
            e.AddCost(new("Included", CostCategory.Service, CostFrequency.Included, null, 1, 12, car));
            e.AddCost(new("Credit", CostCategory.Other, CostFrequency.Once, 80, 3, CandidateId: car, Direction: CostDirection.Income));
        }); return true; });
        var bytes = await f.TransferAsync(owner, t => t.ExportAsync(id)); Assert.Equal(7, JsonNode.Parse(bytes)!["version"]!.GetValue<int>());
        var copyId = await f.TransferAsync(owner, t => t.ImportAsync(bytes)); var copy = await f.AsAsync(owner, w => w.ReadAsync(copyId));
        var original = await f.AsAsync(owner, w => w.ReadAsync(id));
        Assert.Equal(original.CostPlanning!.Start, copy.CostPlanning!.Start); Assert.Equal(18, copy.CostPlanning.Months);
        Assert.All(copy.Costs, cost => Assert.DoesNotContain(original.Costs, c => c.Id == cost.Id));
        Assert.Equal(3, copy.Costs.Count(c => c.CandidateId == copy.Candidates.Single().Id));
        Assert.Equal(370, CostProjection.Calculate(copy, copy.Candidates.Single().Id).Totals.Net);
        Assert.Equal(1, CostProjection.Calculate(copy, copy.Candidates.Single().Id).Totals.MissingItems);
        foreach (var property in new[] { "candidateId", "frequency", "amount" })
        {
            var invalid = JsonNode.Parse(bytes)!; invalid["costs"]![0]!["input"]![property] = property switch { "candidateId" => JsonValue.Create(Guid.NewGuid()), "frequency" => JsonValue.Create("Hourly"), _ => JsonValue.Create(-1) };
            await Assert.ThrowsAsync<RuleException>(() => f.TransferAsync(owner, t => t.ImportAsync(Encoding.UTF8.GetBytes(invalid.ToJsonString()))));
        }
        var legacy = JsonNode.Parse(bytes)!; legacy.AsObject().Remove("calculationSettings"); legacy.AsObject().Remove("scenarios"); legacy["version"] = 4; legacy.AsObject().Remove("costPlanning"); legacy.AsObject().Remove("costs");
        var legacyId = await f.TransferAsync(owner, t => t.ImportAsync(Encoding.UTF8.GetBytes(legacy.ToJsonString())));
        var old = await f.AsAsync(owner, w => w.ReadAsync(legacyId)); Assert.Null(old.CostPlanning); Assert.Empty(old.Costs);
    }
    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Upgrade_keeps_existing_offers_and_does_not_seed_a_plan(int port)
    {
        await using var f = await PostgresFixture.CreateAsync(port); var owner = await f.SetupAsync();
        var e = new Evaluation(owner, "Existing", null); var car = e.AddCandidate(new("Existing car")); var offer = e.AddOffer(car, new() { Name = "Existing offer", BasePrice = 23456 }, [], "EUR");
        await f.RunAsync(async p => {
            var db = p.GetRequiredService<AppDbContext>(); await db.GetService<IMigrator>().MigrateAsync("20261110191928_OfferDocuments");
            db.Evaluations.Add(e); await db.SaveChangesAsync(); await db.Database.MigrateAsync(); return true;
        });
        var saved = await f.AsAsync(owner, w => w.ReadAsync(e.Id)); Assert.Null(saved.CostPlanning); Assert.Empty(saved.Costs);
        Assert.Equal(23456, saved.FindOffer(offer).Latest.Terms.BasePrice); Assert.Equal("EUR", saved.Currency);
    }
}
