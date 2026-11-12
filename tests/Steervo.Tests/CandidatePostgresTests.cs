using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Steervo.Application.Evaluations;
using Steervo.Core;
using Steervo.Infrastructure.Data;
using Xunit;

namespace Steervo.Tests;

[Collection("Postgres")]
public sealed class CandidatePostgresTests
{
    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Assessment_updates_are_authorized_versioned_and_persist_after_reloading(int port)
    {
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync();
        var reader = await fixture.InviteAsync(owner, "reader@example.test");
        var editor = await fixture.InviteAsync(owner, "editor@example.test");
        var caseId = await fixture.AsAsync(owner, w => w.CreateAsync("Decision", null));
        Guid candidate = default, criterion = default;
        await fixture.AsAsync(owner, async w => { await w.EditAsync(caseId, 1, e => {
            candidate = e.AddCandidate(new("Example model"));
            criterion = e.AddCriterion(new("Feature", CriterionType.Boolean, Priority.Required, CriterionScope.Vehicle));
        }); return true; });
        await fixture.AsAsync(owner, async w => { await w.ShareAsync(caseId, 2, "reader@example.test", AccessLevel.Reader); return true; });
        await fixture.AsAsync(owner, async w => { await w.ShareAsync(caseId, 3, "editor@example.test", AccessLevel.Editor); return true; });
        await Assert.ThrowsAsync<RuleException>(() => fixture.AsAsync(reader, async w => { await new CandidateWorkspace(w).AssessAsync(caseId, 4, candidate, criterion, new(BooleanValue: true)); return true; }));
        await fixture.AsAsync(editor, async w => { await new CandidateWorkspace(w).AssessAsync(caseId, 4, candidate, criterion, new(BooleanValue: false, Source: AssessmentSource.TestDrive, Notes: "Observed")); return true; });
        var saved = await fixture.AsAsync(reader, w => w.ReadAsync(caseId));
        Assert.Equal(Fulfillment.Unmet, saved.ResultFor(candidate, criterion));
        Assert.Equal("Observed", saved.Assessments.Single().Notes);
        var assessmentId = saved.Assessments.Single().Id;
        await Assert.ThrowsAsync<RuleException>(() => fixture.AsAsync(editor, async w => { await new CandidateWorkspace(w).AssessAsync(caseId, 4, candidate, criterion, new(BooleanValue: true)); return true; }));
        await fixture.AsAsync(editor, async w => { await new CandidateWorkspace(w).AssessAsync(caseId, 5, candidate, criterion, new(BooleanValue: true)); return true; });
        saved = await fixture.AsAsync(reader, w => w.ReadAsync(caseId));
        Assert.Equal(assessmentId, saved.Assessments.Single().Id);
        Assert.Equal(Fulfillment.Met, saved.ResultFor(candidate, criterion));
        await fixture.AsAsync(owner, async w => { await w.RevokeAsync(caseId, saved.Version, editor); return true; });
        await Assert.ThrowsAsync<RuleException>(() => fixture.AsAsync(editor, async w => { await new CandidateWorkspace(w).ClearAssessmentAsync(caseId, saved.Version + 1, candidate, criterion); return true; }));
    }

    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Migration_preserves_existing_criteria_and_database_rejects_cross_case_assessments(int port)
    {
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync();
        await fixture.RunAsync(async p => { await p.GetRequiredService<AppDbContext>().GetService<IMigrator>().MigrateAsync("20261108211927_InitialCommunity"); return true; });
        var original = new Evaluation(owner, "Existing decision", null);
        var criterion = original.AddCriterion(new("Original requirement", CriterionType.Boolean, Priority.Required, CriterionScope.Vehicle));
        await fixture.RunAsync(async p => {
            var db = p.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Evaluations\" (\"Id\", \"OwnerId\", \"Name\", \"Purpose\", \"Version\", \"CreatedAt\") VALUES ({original.Id}, {owner}, {original.Name}, '', 1, {original.CreatedAt})");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Criterion\" (\"Id\", \"EvaluationId\", \"Name\", \"Type\", \"Priority\", \"Scope\", \"Comparison\", \"Position\") VALUES ({criterion}, {original.Id}, 'Original requirement', 'Boolean', 'Required', 'Vehicle', 'AtLeast', 0)");
            await db.Database.MigrateAsync(); return true;
        });
        var restored = await fixture.AsAsync(owner, w => w.ReadAsync(original.Id));
        Assert.Equal(criterion, restored.Criteria.Single().Id);
        Assert.Empty(restored.Candidates);
        var other = new Evaluation(owner, "Another decision", null);
        var foreignCandidate = other.AddCandidate(new("Other model"));
        await fixture.RunAsync(async p => { var db = p.GetRequiredService<AppDbContext>(); db.Add(other); await db.SaveChangesAsync(); return true; });
        var error = await Assert.ThrowsAsync<PostgresException>(() => fixture.RunAsync(async p => {
            await p.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Assessment\" (\"Id\", \"EvaluationId\", \"CandidateId\", \"CriterionId\", \"Source\", \"Notes\") VALUES ({Guid.NewGuid()}, {original.Id}, {foreignCandidate}, {criterion}, 'Unknown', '')");
            return true;
        }));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }

    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Export_roundtrips_candidates_and_observations_as_independent_records_and_accepts_legacy_files(int port)
    {
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync();
        var id = await fixture.AsAsync(owner, w => w.CreateAsync("Decision", null));
        await fixture.AsAsync(owner, async w => { await w.EditAsync(id, 1, e => {
            var group = e.AddGroup("Group");
            var criterion = e.AddCriterion(new("Impression", CriterionType.Rating, Priority.Required, CriterionScope.Vehicle, group));
            var candidate = e.AddCandidate(new("Example model", Links: "https://example.test/specification"));
            e.ClassifyCandidate(candidate, CandidateStatus.Deferred, "Waiting for evidence");
            e.AssessCandidate(candidate, criterion, new(RatingValue: 2, Source: AssessmentSource.Inspection, Notes: "Personal view"));
        }); return true; });
        var bytes = await fixture.TransferAsync(owner, t => t.ExportAsync(id));
        var copyId = await fixture.TransferAsync(owner, t => t.ImportAsync(bytes));
        var original = await fixture.AsAsync(owner, w => w.ReadAsync(id));
        var copy = await fixture.AsAsync(owner, w => w.ReadAsync(copyId));
        Assert.NotEqual(original.Candidates.Single().Id, copy.Candidates.Single().Id);
        Assert.NotEqual(original.Criteria.Single().Id, copy.Criteria.Single().Id);
        Assert.Equal(CandidateStatus.Deferred, copy.Candidates.Single().Status);
        Assert.Equal("Personal view", copy.Assessments.Single().Notes);
        Assert.Equal(copy.Candidates.Single().Id, copy.Assessments.Single().CandidateId);
        Assert.Equal(copy.Criteria.Single().Id, copy.Assessments.Single().CriterionId);
        Assert.Empty(copy.Shares);
        var invalid = JsonNode.Parse(bytes)!;
        invalid["assessments"]![0]!["candidateId"] = Guid.NewGuid();
        await Assert.ThrowsAsync<RuleException>(() => fixture.TransferAsync(owner, t => t.ImportAsync(Encoding.UTF8.GetBytes(invalid.ToJsonString()))));
        var legacy = """{"format":"steervo-evaluation","version":1,"name":"Legacy decision","purpose":"","groups":[],"criteria":[]}""";
        var legacyId = await fixture.TransferAsync(owner, t => t.ImportAsync(Encoding.UTF8.GetBytes(legacy)));
        Assert.Empty((await fixture.AsAsync(owner, w => w.ReadAsync(legacyId))).Candidates);
    }
}
