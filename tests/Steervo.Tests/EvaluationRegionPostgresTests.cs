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
/// Checks that country and currency of an evaluation are stored, transferred and migrated
/// correctly on PostgreSQL.
/// </summary>
[Collection("Postgres")]
public sealed class EvaluationRegionPostgresTests
{
    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Creation_requires_metadata_and_roundtrips_independent_country_and_currency(
        int port
    )
    {
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync();

        // Without country or currency nothing is created.
        await Assert.ThrowsAsync<RuleException>(() =>
            fixture.AsAsync(owner, workspace => workspace.CreateAsync("Invalid", null, null, "CHF"))
        );
        await Assert.ThrowsAsync<RuleException>(() =>
            fixture.AsAsync(owner, workspace => workspace.CreateAsync("Invalid", null, "CH", null))
        );
        Assert.Empty(await fixture.AsAsync(owner, workspace => workspace.ListAsync()));

        // Country and currency are stored independently of each other.
        var id = await fixture.AsAsync(
            owner,
            workspace => workspace.CreateAsync("Decision", null, "LI", "CHF")
        );
        var original = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(id));
        Assert.Equal("LI", original.Country);
        Assert.Equal("CHF", original.Currency);

        // Both survive export and import.
        var exported = await fixture.TransferAsync(owner, transfer => transfer.ExportAsync(id));
        var json = JsonNode.Parse(exported)!;
        Assert.Equal(9, json["version"]!.GetValue<int>());
        var copyId = await fixture.TransferAsync(owner, transfer => transfer.ImportAsync(exported));
        var copy = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(copyId));
        Assert.Equal("LI", copy.Country);
        Assert.Equal("CHF", copy.Currency);

        // A file with an unknown country is rejected and creates nothing.
        json["country"] = "ZZ";
        await Assert.ThrowsAsync<RuleException>(() => ImportAsync(fixture, owner, json));
        var evaluations = await fixture.AsAsync(owner, workspace => workspace.ListAsync());
        Assert.Equal(2, evaluations.Count);
    }

    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Migration_and_legacy_import_preserve_money_without_guessing_country(int port)
    {
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync();
        var id = await fixture.AsAsync(
            owner,
            workspace => workspace.CreateAsync("Existing", null, "LI", "CHF")
        );
        await EditAsync(
            fixture,
            owner,
            id,
            1,
            evaluation =>
                evaluation.AddOffer(
                    evaluation.AddCandidate(new("Car")),
                    new() { Name = "Offer", StatedTotal = 23456 },
                    []
                )
        );

        // Go back to the schema before the country existed and upgrade again.
        await fixture.RunAsync(async services =>
        {
            var migrator = services.GetRequiredService<AppDbContext>().GetService<IMigrator>();
            await migrator.MigrateAsync("20261113152355_OptionalEquipment");
            await migrator.MigrateAsync();
            return true;
        });

        // The upgrade keeps currency and amounts and leaves the country open.
        var migrated = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(id));
        Assert.Null(migrated.Country);
        Assert.Equal("CHF", migrated.Currency);
        Assert.Equal(23456, migrated.Offers.Single().Latest.Pricing.Total);

        // The same holds for a file of format version 7, which has no country yet.
        var json = JsonNode.Parse(
            await fixture.TransferAsync(owner, transfer => transfer.ExportAsync(id))
        )!;
        json["version"] = 7;
        json.AsObject().Remove("country");
        var copyId = await ImportAsync(fixture, owner, json);
        var copy = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(copyId));
        Assert.Null(copy.Country);
        Assert.Equal("CHF", copy.Currency);

        // The currency is fixed by the offer; a rejected change leaves everything as it was.
        await Assert.ThrowsAsync<RuleException>(() =>
            EditAsync(
                fixture,
                owner,
                id,
                migrated.Version,
                evaluation => evaluation.SetProperties("Wrong", null, "DE", "EUR")
            )
        );
        var unchanged = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(id));
        Assert.Null(unchanged.Country);
        Assert.Equal("Existing", unchanged.Name);
        Assert.Equal(migrated.Version, unchanged.Version);

        // The missing country can be added afterwards.
        await EditAsync(
            fixture,
            owner,
            id,
            migrated.Version,
            evaluation => evaluation.SetProperties(evaluation.Name, evaluation.Purpose, "LI", "CHF")
        );
        var completed = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(id));
        Assert.Equal("LI", completed.Country);
        Assert.Equal(23456, completed.Offers.Single().Latest.Pricing.Total);

        // The version number used above is outdated by now.
        await Assert.ThrowsAsync<RuleException>(() =>
            EditAsync(
                fixture,
                owner,
                id,
                migrated.Version,
                evaluation =>
                    evaluation.SetProperties(evaluation.Name, evaluation.Purpose, "DE", "CHF")
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
        return fixture.AsAsync(
            actor,
            async workspace =>
            {
                await workspace.EditAsync(evaluationId, version, change);
                return true;
            }
        );
    }

    private static Task<Guid> ImportAsync(PostgresFixture fixture, string owner, JsonNode file)
    {
        return fixture.TransferAsync(
            owner,
            transfer => transfer.ImportAsync(Encoding.UTF8.GetBytes(file.ToJsonString()))
        );
    }
}
