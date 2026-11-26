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

/// <summary>
/// Checks that running costs are stored, shared, transferred and migrated correctly on PostgreSQL.
/// </summary>
[Collection("Postgres")]
public sealed class CostPostgresTests
{
    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Costs_enforce_sharing_conflicts_and_cross_case_references(int port)
    {
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync();
        var reader = await fixture.InviteAsync(owner, "reader@example.test");
        var editor = await fixture.InviteAsync(owner, "editor@example.test");
        var id = await fixture.AsAsync(owner, workspace =>
            workspace.CreateAsync("Planning", null, "CH", "EUR")
        );
        Guid car = default;
        await EditAsync(fixture, owner, id, 1, evaluation =>
        {
            car = evaluation.AddCandidate(new("Vehicle"));
            evaluation.AddOffer(car, new() { Name = "Offer" }, [], "EUR");
        });
        await fixture.AsAsync(owner, async workspace =>
        {
            await workspace.ShareAsync(id, 2, "reader@example.test", AccessLevel.Reader);
            await workspace.ShareAsync(id, 3, "editor@example.test", AccessLevel.Editor);
            return true;
        });

        // Only an editor may set up the cost plan and add costs.
        await Assert.ThrowsAsync<RuleException>(() =>
            CostsAsync(fixture, reader, costs => costs.PlanAsync(id, 4, new(2027, 1, 1), 24))
        );
        await CostsAsync(fixture, editor, costs => costs.PlanAsync(id, 4, new(2027, 1, 1), 24));
        var cost = await fixture.AsAsync(editor, workspace =>
            new CostWorkspace(workspace).AddAsync(
                id,
                5,
                new("Cover", CostCategory.Insurance, CostFrequency.Yearly, 180, CandidateId: car)
            )
        );

        // Two annual payments of 180 fall into the 24 months of the plan.
        var saved = await fixture.AsAsync(reader, workspace => workspace.ReadAsync(id));
        Assert.Equal(360, CostProjection.Calculate(saved, car).Totals.Net);

        // A reader may not remove the cost, and an outdated version number is rejected.
        await Assert.ThrowsAsync<RuleException>(() =>
            CostsAsync(fixture, reader, costs => costs.RemoveAsync(id, 6, cost))
        );
        await Assert.ThrowsAsync<RuleException>(() =>
            CostsAsync(fixture, editor, costs =>
                costs.EditAsync(id, 5, cost, saved.FindCost(cost).ToInput() with { Amount = 1 })
            )
        );

        // A cost cannot belong to the car of another evaluation, neither through the
        // application nor directly in the database.
        var other = new Evaluation(owner, "Other", null);
        var foreignCar = other.AddCandidate(new("Other car"));
        await fixture.RunAsync(async services =>
        {
            await services.GetRequiredService<IEvaluationStore>().CreateAsync(other);
            return true;
        });
        await Assert.ThrowsAsync<RuleException>(() =>
            fixture.AsAsync(editor, workspace =>
                new CostWorkspace(workspace).AddAsync(
                    id,
                    6,
                    saved.FindCost(cost).ToInput() with
                    {
                        CandidateId = foreignCar,
                    }
                )
            )
        );
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            fixture.RunAsync(async services =>
            {
                await services
                    .GetRequiredService<AppDbContext>()
                    .Database.ExecuteSqlInterpolatedAsync(
                        $"UPDATE \"CostItem\" SET \"CandidateId\" = {foreignCar} WHERE \"Id\" = {cost}"
                    );
                return true;
            })
        );
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);

        // A revoked editor loses access.
        await fixture.AsAsync(owner, async workspace =>
        {
            await workspace.RevokeAsync(id, 6, editor);
            return true;
        });
        await Assert.ThrowsAsync<RuleException>(() =>
            fixture.AsAsync(editor, workspace => workspace.ReadAsync(id))
        );

        // Removing the car removes its costs.
        await EditAsync(fixture, owner, id, 7, evaluation => evaluation.RemoveCandidate(car));
        var afterRemoval = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(id));
        Assert.Empty(afterRemoval.Costs);
    }

    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Transfer_preserves_unknown_included_timing_and_independent_candidate_costs(
        int port
    )
    {
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync();
        var id = await fixture.AsAsync(owner, workspace =>
            workspace.CreateAsync("Planning", null, "CH", "EUR")
        );
        await EditAsync(fixture, owner, id, 1, AddCarWithEveryKindOfCost);

        // Export and import as a new evaluation.
        var exported = await fixture.TransferAsync(owner, transfer => transfer.ExportAsync(id));
        Assert.Equal(9, JsonNode.Parse(exported)!["version"]!.GetValue<int>());
        var copyId = await fixture.TransferAsync(owner, transfer => transfer.ImportAsync(exported));
        var copy = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(copyId));
        var original = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(id));

        Assert.Equal(original.CostPlanning!.Start, copy.CostPlanning!.Start);
        Assert.Equal(18, copy.CostPlanning.Months);
        Assert.All(copy.Costs, cost => Assert.DoesNotContain(original.Costs, c => c.Id == cost.Id));
        Assert.Equal(3, copy.Costs.Count(c => c.CandidateId == copy.Candidates.Single().Id));

        // 18 x 25 shared - 80 credit; the annual cost without an amount stays missing.
        Assert.Equal(370, CostProjection.Calculate(copy, copy.Candidates.Single().Id).Totals.Net);
        Assert.Equal(
            1,
            CostProjection.Calculate(copy, copy.Candidates.Single().Id).Totals.MissingItems
        );

        // A file with an invalid cost is rejected.
        foreach (var property in new[] { "candidateId", "frequency", "amount" })
        {
            var invalid = JsonNode.Parse(exported)!;
            invalid["costs"]![0]!["input"]![property] = property switch
            {
                "candidateId" => JsonValue.Create(Guid.NewGuid()),
                "frequency" => JsonValue.Create("Hourly"),
                _ => JsonValue.Create(-1),
            };

            await Assert.ThrowsAsync<RuleException>(() => ImportAsync(fixture, owner, invalid));
        }

        // A file of format version 4 knows neither a cost plan nor costs.
        var legacy = JsonNode.Parse(exported)!;
        legacy.AsObject().Remove("calculationSettings");
        legacy.AsObject().Remove("scenarios");
        legacy["version"] = 4;
        legacy.AsObject().Remove("country");
        legacy.AsObject().Remove("costPlanning");
        legacy.AsObject().Remove("costs");
        var legacyId = await ImportAsync(fixture, owner, legacy);
        var importedLegacy = await fixture.AsAsync(owner, workspace =>
            workspace.ReadAsync(legacyId)
        );

        Assert.Null(importedLegacy.CostPlanning);
        Assert.Empty(importedLegacy.Costs);
    }

    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Upgrade_keeps_existing_offers_and_does_not_seed_a_plan(int port)
    {
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync();
        var evaluation = new Evaluation(owner, "Existing", null);
        var car = evaluation.AddCandidate(new("Existing car"));
        var offer = evaluation.AddOffer(
            car,
            new() { Name = "Existing offer", StatedTotal = 23456 },
            [],
            "EUR"
        );

        // Store the evaluation, go back to the schema before cost planning and upgrade again.
        await fixture.RunAsync(async services =>
        {
            var database = services.GetRequiredService<AppDbContext>();
            database.Evaluations.Add(evaluation);
            await database.SaveChangesAsync();
            await database.GetService<IMigrator>().MigrateAsync("20261110191928_OfferDocuments");
            await database.Database.MigrateAsync();
            return true;
        });

        var saved = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(evaluation.Id));

        Assert.Null(saved.CostPlanning);
        Assert.Empty(saved.Costs);
        Assert.Null(saved.FindOffer(offer).Latest.Terms.StatedTotal);
        Assert.Equal(23456, saved.FindOffer(offer).Latest.Pricing.Total);
        Assert.Equal("EUR", saved.Currency);
    }

    /// <summary>
    /// Adds a car and an 18-month plan with a shared cost, a cost without an amount,
    /// an included cost and a one-off income.
    /// </summary>
    private static void AddCarWithEveryKindOfCost(Evaluation evaluation)
    {
        var car = evaluation.AddCandidate(new("Vehicle"));
        evaluation.SetCurrency("EUR");
        evaluation.SetCostPlanning(new(2027, 10, 1), 18);
        evaluation.AddCost(new("Shared", CostCategory.Parking, CostFrequency.Monthly, 25));
        evaluation.AddCost(
            new("Unknown", CostCategory.Insurance, CostFrequency.Yearly, null, 2, 15, car)
        );
        evaluation.AddCost(
            new("Included", CostCategory.Service, CostFrequency.Included, null, 1, 12, car)
        );
        evaluation.AddCost(
            new(
                "Credit",
                CostCategory.Other,
                CostFrequency.Once,
                80,
                3,
                CandidateId: car,
                Direction: CostDirection.Income
            )
        );
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

    private static Task<bool> CostsAsync(
        PostgresFixture fixture,
        string actor,
        Func<CostWorkspace, Task> action
    )
    {
        return fixture.AsAsync(actor, async workspace =>
        {
            await action(new CostWorkspace(workspace));
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
