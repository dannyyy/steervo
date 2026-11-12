using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Steervo.Application.Offers;
using Steervo.Core;
using Steervo.Infrastructure.Data;
using Xunit;

namespace Steervo.Tests;

[Collection("Postgres")]
public sealed class OfferPostgresTests
{
    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Revisions_are_persistent_append_only_authorized_and_versioned(int port)
    {
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync(); var reader = await fixture.InviteAsync(owner, "reader@example.test"); var editor = await fixture.InviteAsync(owner, "editor@example.test");
        var id = await fixture.AsAsync(owner, workspace => workspace.CreateAsync("Decision", null));
        Guid candidate = default, offerId = default;
        await fixture.AsAsync(owner, async workspace => { await workspace.EditAsync(id, 1, evaluation => candidate = evaluation.AddCandidate(new("Vehicle"))); return true; });
        await fixture.AsAsync(owner, async workspace => { await workspace.ShareAsync(id, 2, "reader@example.test", AccessLevel.Reader); return true; });
        await fixture.AsAsync(owner, async workspace => { await workspace.ShareAsync(id, 3, "editor@example.test", AccessLevel.Editor); return true; });
        var terms = new OfferTerms { Name = "Request", Kind = OfferKind.Leasing, Provider = "Provider", MonthlyPayment = 640, AnnualInterestPercent = 2.75m, TermMonths = 24 };
        await Assert.ThrowsAsync<RuleException>(() => fixture.AsAsync(reader, workspace => new OfferWorkspace(workspace).AddAsync(id, 4, candidate, terms, [], "EUR", null)));
        offerId = await fixture.AsAsync(editor, workspace => new OfferWorkspace(workspace).AddAsync(id, 4, candidate, terms, [new("Fee", OfferLineKind.Fee, OfferPriceKind.Amount, 275)], "EUR", null));
        await fixture.AsAsync(editor, async workspace => { await new OfferWorkspace(workspace).ReviseAsync(id, 5, offerId, terms with { MonthlyPayment = 610 }, [new("Fee", OfferLineKind.Fee, OfferPriceKind.Included)], "Negotiated"); return true; });
        var saved = await fixture.AsAsync(reader, workspace => workspace.ReadAsync(id));
        var offer = saved.FindOffer(offerId);
        Assert.Equal("EUR", saved.Currency);
        Assert.Equal(640, offer.Revision(1).Terms.MonthlyPayment);
        Assert.Equal(275, offer.Revision(1).Lines.Single().Amount);
        Assert.Equal(610, offer.Latest.Terms.MonthlyPayment);
        await Assert.ThrowsAsync<RuleException>(() => fixture.AsAsync(editor, async workspace => { await new OfferWorkspace(workspace).ReviseAsync(id, 5, offerId, terms, [], null); return true; }));
        var revisionError = await Assert.ThrowsAsync<PostgresException>(() => fixture.RunAsync(async provider => {
            await provider.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync($"UPDATE \"OfferRevision\" SET \"Terms_MonthlyPayment\" = 1 WHERE \"Id\" = {offer.Revision(1).Id}"); return true;
        }));
        Assert.Equal(PostgresErrorCodes.CheckViolation, revisionError.SqlState);
        var lineError = await Assert.ThrowsAsync<PostgresException>(() => fixture.RunAsync(async provider => {
            await provider.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync($"UPDATE \"OfferLine\" SET \"Amount\" = 1 WHERE \"Id\" = {offer.Revision(1).Lines.Single().Id}"); return true;
        }));
        Assert.Equal(PostgresErrorCodes.CheckViolation, lineError.SqlState);
        await fixture.AsAsync(owner, async workspace => { await workspace.RevokeAsync(id, saved.Version, editor); return true; });
        await Assert.ThrowsAsync<RuleException>(() => fixture.AsAsync(editor, async workspace => { await new OfferWorkspace(workspace).ArchiveAsync(id, saved.Version + 1, offerId, true); return true; }));
    }

    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Export_remaps_offer_history_and_retains_stale_assessments_and_archives(int port)
    {
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync();
        var id = await fixture.AsAsync(owner, workspace => workspace.CreateAsync("Decision", null));
        await fixture.AsAsync(owner, async workspace => { await workspace.EditAsync(id, 1, evaluation => {
            var candidate = evaluation.AddCandidate(new("Vehicle"));
            var criterion = evaluation.AddCriterion(new("Service", CriterionType.Boolean, Priority.Required, CriterionScope.Offer));
            var offer = evaluation.AddOffer(candidate, new() { Name = "Request" }, [new("Service", OfferLineKind.Benefit, OfferPriceKind.Included, Effect: OfferBenefitEffect.CostSaving, CostCategory: CostCategory.Service, DurationMonths: 24)], "EUR");
            evaluation.AssessOffer(offer, criterion, new(BooleanValue: true, Notes: "Confirmed"));
            evaluation.ReviseOffer(offer, new() { Name = "Negotiated", BasePrice = 27800 }, [], "Completed");
            evaluation.ArchiveOffer(offer, true);
        }); return true; });
        var original = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(id));
        var bytes = await fixture.TransferAsync(owner, transfer => transfer.ExportAsync(id));
        var copyId = await fixture.TransferAsync(owner, transfer => transfer.ImportAsync(bytes));
        var copy = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(copyId));
        var offer = copy.Offers.Single(); var originalOffer = original.Offers.Single();
        Assert.True(offer.Archived); Assert.Equal("EUR", copy.Currency);
        Assert.NotEqual(originalOffer.Id, offer.Id); Assert.NotEqual(originalOffer.CandidateId, offer.CandidateId);
        Assert.Equal(originalOffer.Revision(1).CreatedAt, offer.Revision(1).CreatedAt);
        Assert.Equal(originalOffer.Revision(1).Terms, offer.Revision(1).Terms);
        Assert.NotEqual(originalOffer.Revision(1).Lines.Single().Id, offer.Revision(1).Lines.Single().Id);
        Assert.Equal(originalOffer.Revision(1).Lines.Single().ToInput(), offer.Revision(1).Lines.Single().ToInput());
        Assert.Equal(1, copy.OfferAssessments.Single().RevisionNumber);
        Assert.Equal(Fulfillment.Unknown, copy.OfferResult(offer.Id, copy.Criteria.Single().Id));
        Assert.Empty(copy.Shares);
        foreach (var property in new[] { "candidateId", "id" })
        {
            var invalid = JsonNode.Parse(bytes)!; invalid["offers"]![0]![property] = Guid.Empty;
            await Assert.ThrowsAsync<RuleException>(() => fixture.TransferAsync(owner, transfer => transfer.ImportAsync(Encoding.UTF8.GetBytes(invalid.ToJsonString()))));
        }
        var malformed = JsonNode.Parse(bytes)!; malformed["offers"]![0]!["revisions"]![1]!["number"] = 4;
        await Assert.ThrowsAsync<RuleException>(() => fixture.TransferAsync(owner, transfer => transfer.ImportAsync(Encoding.UTF8.GetBytes(malformed.ToJsonString()))));
        var legacy = JsonNode.Parse(bytes)!; legacy.AsObject().Remove("calculationSettings"); legacy.AsObject().Remove("scenarios"); legacy["version"] = 3;
        foreach (var property in new[] { "currency", "offers", "offerAssessments", "costPlanning", "costs" }) legacy.AsObject().Remove(property);
        var legacyId = await fixture.TransferAsync(owner, transfer => transfer.ImportAsync(Encoding.UTF8.GetBytes(legacy.ToJsonString())));
        var old = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(legacyId));
        Assert.Single(old.Candidates); Assert.Empty(old.Offers); Assert.Null(old.Currency);
    }

    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Database_rejects_cross_case_offers_and_revision_assessments_and_candidate_deletion_cascades(int port)
    {
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync();
        var first = new Evaluation(owner, "First", null); var second = new Evaluation(owner, "Second", null);
        var candidate = first.AddCandidate(new("Vehicle")); var foreignCandidate = second.AddCandidate(new("Other"));
        var criterion = first.AddCriterion(new("Service", CriterionType.Boolean, Priority.Required, CriterionScope.Offer));
        var foreignCriterion = second.AddCriterion(new("Foreign", CriterionType.Boolean, Priority.Required, CriterionScope.Offer));
        var offer = first.AddOffer(candidate, new() { Name = "Request" }, [new("Fee")], "EUR");
        first.AssessOffer(offer, criterion, new(BooleanValue: false));
        await fixture.RunAsync(async provider => { var db = provider.GetRequiredService<AppDbContext>(); db.AddRange(first, second); await db.SaveChangesAsync(); return true; });
        var error = await Assert.ThrowsAsync<PostgresException>(() => fixture.RunAsync(async provider => {
            await provider.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Offer\" (\"Id\", \"EvaluationId\", \"CandidateId\", \"Archived\") VALUES ({Guid.NewGuid()}, {first.Id}, {foreignCandidate}, false)"); return true;
        }));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
        var assessmentError = await Assert.ThrowsAsync<PostgresException>(() => fixture.RunAsync(async provider => {
            await provider.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"OfferAssessment\" (\"Id\", \"EvaluationId\", \"OfferId\", \"CriterionId\", \"RevisionNumber\", \"Source\", \"Notes\") VALUES ({Guid.NewGuid()}, {first.Id}, {offer}, {foreignCriterion}, 1, 'Unknown', '')"); return true;
        }));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, assessmentError.SqlState);
        await fixture.AsAsync(owner, async workspace => { await workspace.EditAsync(first.Id, 1, evaluation => evaluation.RemoveCandidate(candidate)); return true; });
        var saved = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(first.Id));
        Assert.Empty(saved.Candidates); Assert.Empty(saved.Offers); Assert.Empty(saved.OfferAssessments);
        await fixture.RunAsync(async provider => { var db = provider.GetRequiredService<AppDbContext>(); Assert.Empty(await db.Set<OfferRevision>().ToListAsync()); Assert.Empty(await db.Set<OfferLine>().ToListAsync()); return true; });
    }

    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Migration_retains_existing_specifications_and_starts_with_no_offers_or_currency(int port)
    {
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync();
        var id = await fixture.AsAsync(owner, workspace => workspace.CreateAsync("Existing", null));
        await fixture.AsAsync(owner, async workspace => { await workspace.EditAsync(id, 1, evaluation => evaluation.AddCandidate(new("Vehicle", Specifications: new() { PowerKw = 125, KerbWeightKg = 1800, GrossWeightKg = 2250 }))); return true; });
        await fixture.RunAsync(async provider => {
            var migrator = provider.GetRequiredService<AppDbContext>().GetService<IMigrator>();
            await migrator.MigrateAsync("20261110181930_VehicleSpecifications"); await migrator.MigrateAsync(); return true;
        });
        var saved = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(id));
        Assert.Equal(450, saved.Candidates.Single().Specifications.GetPayloadKg());
        Assert.Empty(saved.Offers); Assert.Null(saved.Currency);
    }
}
