using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Steervo.Application.Evaluations;
using Steervo.Core;
using Steervo.Infrastructure.Data;
using Xunit;

namespace Steervo.Tests;

[Collection("Postgres")]
public sealed class EvaluationRegionPostgresTests
{
    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Creation_requires_metadata_and_roundtrips_independent_country_and_currency(int port)
    {
        await using var f = await PostgresFixture.CreateAsync(port); var owner = await f.SetupAsync();
        await Assert.ThrowsAsync<RuleException>(() => f.AsAsync(owner, w => w.CreateAsync("Invalid", null, null, "CHF")));
        await Assert.ThrowsAsync<RuleException>(() => f.AsAsync(owner, w => w.CreateAsync("Invalid", null, "CH", null)));
        Assert.Empty(await f.AsAsync(owner, w => w.ListAsync()));
        var id = await f.AsAsync(owner, w => w.CreateAsync("Decision", null, "LI", "CHF"));
        var original = await f.AsAsync(owner, w => w.ReadAsync(id));
        Assert.Equal("LI", original.Country); Assert.Equal("CHF", original.Currency);
        var bytes = await f.TransferAsync(owner, t => t.ExportAsync(id));
        var json = JsonNode.Parse(bytes)!; Assert.Equal(9, json["version"]!.GetValue<int>());
        var copyId = await f.TransferAsync(owner, t => t.ImportAsync(bytes));
        var copy = await f.AsAsync(owner, w => w.ReadAsync(copyId));
        Assert.Equal("LI", copy.Country); Assert.Equal("CHF", copy.Currency);
        json["country"] = "ZZ";
        await Assert.ThrowsAsync<RuleException>(() => f.TransferAsync(owner, t => t.ImportAsync(Encoding.UTF8.GetBytes(json.ToJsonString()))));
        Assert.Equal(2, (await f.AsAsync(owner, w => w.ListAsync())).Count);
    }

    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Migration_and_legacy_import_preserve_money_without_guessing_country(int port)
    {
        await using var f = await PostgresFixture.CreateAsync(port); var owner = await f.SetupAsync();
        var id = await f.AsAsync(owner, w => w.CreateAsync("Existing", null, "LI", "CHF"));
        await f.AsAsync(owner, async w => { await w.EditAsync(id, 1, e => e.AddOffer(e.AddCandidate(new("Car")), new() { Name = "Offer", StatedTotal = 23456 }, [])); return true; });
        await f.RunAsync(async p => {
            var migrator = p.GetRequiredService<AppDbContext>().GetService<IMigrator>();
            await migrator.MigrateAsync("20261113152355_OptionalEquipment");
            await migrator.MigrateAsync(); return true;
        });
        var e = await f.AsAsync(owner, w => w.ReadAsync(id));
        Assert.Null(e.Country); Assert.Equal("CHF", e.Currency); Assert.Equal(23456, e.Offers.Single().Latest.Pricing.Total);
        var json = JsonNode.Parse(await f.TransferAsync(owner, t => t.ExportAsync(id)))!;
        json["version"] = 7; json.AsObject().Remove("country");
        var copyId = await f.TransferAsync(owner, t => t.ImportAsync(Encoding.UTF8.GetBytes(json.ToJsonString())));
        var copy = await f.AsAsync(owner, w => w.ReadAsync(copyId)); Assert.Null(copy.Country); Assert.Equal("CHF", copy.Currency);
        await Assert.ThrowsAsync<RuleException>(() => f.AsAsync(owner, async w => { await w.EditAsync(id, e.Version, x => x.SetProperties("Wrong", null, "DE", "EUR")); return true; }));
        var unchanged = await f.AsAsync(owner, w => w.ReadAsync(id)); Assert.Null(unchanged.Country); Assert.Equal("Existing", unchanged.Name); Assert.Equal(e.Version, unchanged.Version);
        await f.AsAsync(owner, async w => { await w.EditAsync(id, e.Version, x => x.SetProperties(x.Name, x.Purpose, "LI", "CHF")); return true; });
        var completed = await f.AsAsync(owner, w => w.ReadAsync(id)); Assert.Equal("LI", completed.Country); Assert.Equal(23456, completed.Offers.Single().Latest.Pricing.Total);
        await Assert.ThrowsAsync<RuleException>(() => f.AsAsync(owner, async w => { await w.EditAsync(id, e.Version, x => x.SetProperties(x.Name, x.Purpose, "DE", "CHF")); return true; }));
    }
}
