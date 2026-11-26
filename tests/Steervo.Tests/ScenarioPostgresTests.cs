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

/// <summary>
/// Checks that scenarios are stored, shared, transferred and migrated correctly on PostgreSQL.
/// </summary>
[Collection("Postgres")]
public sealed class ScenarioPostgresTests
{
    private static ScenarioTerms PurchaseTerms =>
        new()
        {
            Mode = AcquisitionMode.Purchase,
            PurchasePrice = 1500,
            InitialFees = 0,
            CostsReviewed = true,
        };

    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Scenario_mutations_preserve_pinned_versions_enforce_roles_and_cascade_safely(
        int port
    )
    {
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync();
        var reader = await fixture.InviteAsync(owner, "reader@example.test");
        var editor = await fixture.InviteAsync(owner, "editor@example.test");
        var evaluation = CreateEvaluation(owner);
        var id = evaluation.Id;
        var offer = evaluation.Offers.Single().Id;
        var car = evaluation.Candidates.Single().Id;
        await StoreAsync(fixture, evaluation);
        await fixture.AsAsync(owner, async workspace =>
        {
            await workspace.ShareAsync(id, 1, "reader@example.test", AccessLevel.Reader);
            await workspace.ShareAsync(id, 2, "editor@example.test", AccessLevel.Editor);
            return true;
        });

        // A reader may not add scenarios.
        await Assert.ThrowsAsync<RuleException>(() =>
            fixture.AsAsync(reader, workspace =>
                new ScenarioWorkspace(workspace).AddAsync(
                    id,
                    3,
                    new("Denied", offer, 1, PurchaseTerms)
                )
            )
        );

        // An editor sets up a cash scenario with a residual value and a cost adjustment.
        await ScenariosAsync(fixture, editor, scenarios => scenarios.SettingsAsync(id, 3, 0));
        var scenarioId = await fixture.AsAsync(editor, workspace =>
            new ScenarioWorkspace(workspace).AddAsync(id, 4, new("Cash", offer, 1, PurchaseTerms))
        );
        await ScenariosAsync(fixture, editor, scenarios =>
            scenarios.ResidualAsync(id, 5, scenarioId, new(12, 500, 600, 700))
        );
        await ScenariosAsync(fixture, editor, scenarios =>
            scenarios.AdjustmentAsync(id, 6, scenarioId, new(evaluation.Costs.Single().Id, 0, 1, 6))
        );

        // 1500 price + 6 x 20 shared cost (months 1 to 6 are waived) - 600 residual.
        var saved = await fixture.AsAsync(reader, workspace => workspace.ReadAsync(id));
        Assert.Equal(1020, ScenarioCalculation.Calculate(saved, scenarioId, 12).ExpectedCost);

        // An outdated version number is rejected, and a reader may not remove scenarios.
        await Assert.ThrowsAsync<RuleException>(() =>
            ScenariosAsync(fixture, editor, scenarios =>
                scenarios.ResidualAsync(id, 5, scenarioId, new(12, 0, 0, 0))
            )
        );
        await Assert.ThrowsAsync<RuleException>(() =>
            ScenariosAsync(fixture, reader, scenarios => scenarios.RemoveAsync(id, 7, scenarioId))
        );

        // A scenario can refer neither to the offer nor to a cost of another evaluation.
        var foreign = CreateEvaluation(owner);
        await StoreAsync(fixture, foreign);
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteSqlAsync(
                fixture,
                $"UPDATE \"Scenario\" SET \"OfferId\" = {foreign.Offers.Single().Id} WHERE \"Id\" = {scenarioId}"
            )
        );
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
        await Assert.ThrowsAsync<RuleException>(() =>
            ScenariosAsync(fixture, editor, scenarios =>
                scenarios.AdjustmentAsync(id, 7, scenarioId, new(foreign.Costs.Single().Id, 0))
            )
        );

        // Revising the offer leaves the scenario on the version it was built on.
        await EditAsync(fixture, owner, id, 7, current =>
            current.ReviseOffer(offer, new() { Name = "Revised", StatedTotal = 2000 }, [])
        );
        saved = await fixture.AsAsync(reader, workspace => workspace.ReadAsync(id));
        Assert.Equal(1, saved.FindScenario(scenarioId).RevisionNumber);
        Assert.Equal(1020, ScenarioCalculation.Calculate(saved, scenarioId, 12).ExpectedCost);

        // A revoked editor loses access.
        await fixture.AsAsync(owner, async workspace =>
        {
            await workspace.RevokeAsync(id, 8, editor);
            return true;
        });
        await Assert.ThrowsAsync<RuleException>(() =>
            fixture.AsAsync(editor, workspace => workspace.ReadAsync(id))
        );

        // Removing the car removes its scenarios with all their rows, but not the shared cost.
        await EditAsync(fixture, owner, id, 9, current => current.RemoveCandidate(car));
        saved = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(id));
        Assert.Empty(saved.Scenarios);
        Assert.Single(saved.Costs);
        await fixture.RunAsync(async services =>
        {
            var database = services.GetRequiredService<AppDbContext>();
            Assert.Empty(await database.Set<ScenarioCostAdjustment>().ToArrayAsync());
            Assert.Empty(await database.Set<ResidualCheckpoint>().ToArrayAsync());
            return true;
        });
    }

    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Scenario_transfer_remaps_sources_and_costs_and_preserves_incomplete_assumptions(
        int port
    )
    {
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync();

        // One complete scenario and a copy whose purchase price is still unknown.
        var evaluation = CreateEvaluation(owner);
        evaluation.SetCalculationSettings(3.25m);
        var offer = evaluation.Offers.Single().Id;
        var scenario = evaluation.AddScenario(
            new("Cash", offer, 1, PurchaseTerms, "Independent assumptions")
        );
        evaluation.SetResidual(scenario, new(12, null, 600, 700, "Source note"));
        evaluation.SetCostAdjustment(
            scenario,
            new(evaluation.Costs.Single().Id, 0, 1, 4, "Included period")
        );
        var scenarioWithoutPrice = evaluation.CopyScenario(scenario, "Unknown price");
        evaluation.EditScenario(
            scenarioWithoutPrice,
            evaluation.FindScenario(scenarioWithoutPrice).ToInput() with
            {
                Terms = PurchaseTerms with { PurchasePrice = null },
            }
        );
        await StoreAsync(fixture, evaluation);

        // Export and import as a new evaluation.
        var exported = await fixture.TransferAsync(owner, transfer =>
            transfer.ExportAsync(evaluation.Id)
        );
        Assert.Equal(9, JsonNode.Parse(exported)!["version"]!.GetValue<int>());
        var importedId = await fixture.TransferAsync(owner, transfer =>
            transfer.ImportAsync(exported)
        );
        var imported = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(importedId));

        Assert.Equal(3.25m, imported.CalculationSettings!.AnnualReturnPercent);
        Assert.Equal(2, imported.Scenarios.Count);
        Assert.Empty(imported.Shares);

        // The imported scenarios have new ids and point to the imported offer and cost.
        foreach (var importedScenario in imported.Scenarios)
        {
            Assert.NotEqual(offer, importedScenario.OfferId);
            Assert.DoesNotContain(evaluation.Scenarios, original =>
                original.Id == importedScenario.Id
            );
            Assert.Equal(
                imported.Costs.Single().Id,
                importedScenario.CostAdjustments.Single().CostId
            );
            Assert.Null(importedScenario.Residuals.Single().Low);
        }

        Assert.Null(imported.Scenarios.Single(s => s.Name == "Unknown price").Terms.PurchasePrice);
        var importedCash = imported.Scenarios.Single(s => s.Name == "Cash");
        Assert.Equal(
            ScenarioCalculation.Calculate(evaluation, scenario, 12).ExpectedCost,
            ScenarioCalculation.Calculate(imported, importedCash.Id, 12).ExpectedCost
        );

        // A file with a broken scenario is rejected as a whole.
        foreach (var defect in new[] { "source", "cost", "band", "duplicate", "mode" })
        {
            var broken = JsonNode.Parse(exported)!;
            BreakFirstScenario(broken, defect);

            await Assert.ThrowsAsync<RuleException>(() => ImportAsync(fixture, owner, broken));
        }

        // A file of format version 5 knows neither scenarios nor calculation settings.
        var legacy = JsonNode.Parse(exported)!;
        legacy["version"] = 5;
        legacy.AsObject().Remove("country");
        legacy.AsObject().Remove("calculationSettings");
        legacy.AsObject().Remove("scenarios");
        var legacyId = await ImportAsync(fixture, owner, legacy);
        var importedLegacy = await fixture.AsAsync(owner, workspace =>
            workspace.ReadAsync(legacyId)
        );

        Assert.Empty(importedLegacy.Scenarios);
        Assert.Null(importedLegacy.CalculationSettings);
        Assert.Single(importedLegacy.Costs);
    }

    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Upgrade_preserves_existing_costs_and_database_rejects_invalid_bands(int port)
    {
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync();
        var evaluation = CreateEvaluation(owner);

        // Store the evaluation, go back to the schema before scenarios and upgrade again.
        await fixture.RunAsync(async services =>
        {
            var database = services.GetRequiredService<AppDbContext>();
            database.Evaluations.Add(evaluation);
            await database.SaveChangesAsync();
            await database.GetService<IMigrator>().MigrateAsync("20261110195608_CostPlanning");
            await database.Database.MigrateAsync();
            return true;
        });

        var saved = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(evaluation.Id));
        Assert.Empty(saved.Scenarios);
        Assert.Null(saved.CalculationSettings);
        Assert.Equal(20, saved.Costs.Single().Amount);

        // The upgraded schema accepts scenarios, but no residual band with low above expected.
        Guid scenario = default;
        await EditAsync(fixture, owner, evaluation.Id, 1, current =>
        {
            scenario = current.AddScenario(
                new("Cash", current.Offers.Single().Id, 1, PurchaseTerms)
            );
            current.SetResidual(scenario, new(12, 500, 600, 700));
        });
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteSqlAsync(
                fixture,
                $"UPDATE \"ResidualCheckpoint\" SET \"Low\" = 999 WHERE \"ScenarioId\" = {scenario}"
            )
        );
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    /// <summary>
    /// An evaluation with one car, one offer, a 24-month cost plan and one shared monthly cost.
    /// </summary>
    private static Evaluation CreateEvaluation(string owner)
    {
        var evaluation = new Evaluation(owner, "Scenario case", null);
        var car = evaluation.AddCandidate(new("Vehicle"));
        evaluation.AddOffer(car, new() { Name = "Source", StatedTotal = 1700 }, [], "EUR");
        evaluation.SetCostPlanning(new(2027, 10, 1), 24);
        evaluation.AddCost(new("Shared", CostCategory.Parking, CostFrequency.Monthly, 20));

        return evaluation;
    }

    private static Task<bool> StoreAsync(PostgresFixture fixture, Evaluation evaluation)
    {
        return fixture.RunAsync(async services =>
        {
            await services.GetRequiredService<IEvaluationStore>().CreateAsync(evaluation);
            return true;
        });
    }

    private static Task<bool> ScenariosAsync(
        PostgresFixture fixture,
        string actor,
        Func<ScenarioWorkspace, Task> action
    )
    {
        return fixture.AsAsync(actor, async workspace =>
        {
            await action(new ScenarioWorkspace(workspace));
            return true;
        });
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

    private static Task<bool> ExecuteSqlAsync(PostgresFixture fixture, FormattableString sql)
    {
        return fixture.RunAsync(async services =>
        {
            await services.GetRequiredService<AppDbContext>().Database.ExecuteSqlAsync(sql);
            return true;
        });
    }

    private static Task<Guid> ImportAsync(PostgresFixture fixture, string owner, JsonNode file)
    {
        return fixture.TransferAsync(owner, transfer =>
            transfer.ImportAsync(Encoding.UTF8.GetBytes(file.ToJsonString()))
        );
    }

    private static void BreakFirstScenario(JsonNode file, string defect)
    {
        var scenario = file["scenarios"]![0]!;

        switch (defect)
        {
            case "source":
                scenario["input"]!["offerId"] = Guid.NewGuid();
                break;
            case "cost":
                scenario["costAdjustments"]![0]!["costId"] = Guid.NewGuid();
                break;
            case "band":
                scenario["residuals"]![0]!["low"] = 800;
                break;
            case "duplicate":
                scenario["residuals"]!.AsArray().Add(scenario["residuals"]![0]!.DeepClone());
                break;
            case "mode":
                scenario["input"]!["terms"]!["mode"] = "Unrecognized";
                break;
        }
    }
}
