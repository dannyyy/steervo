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

/// <summary>
/// Checks that cars and their assessments are stored, shared, transferred and migrated correctly
/// on PostgreSQL.
/// </summary>
[Collection("Postgres")]
public sealed class CandidatePostgresTests
{
    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Assessment_updates_are_authorized_versioned_and_persist_after_reloading(
        int port
    )
    {
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync();
        var reader = await fixture.InviteAsync(owner, "reader@example.test");
        var editor = await fixture.InviteAsync(owner, "editor@example.test");
        var caseId = await fixture.AsAsync(owner, workspace =>
            workspace.CreateAsync("Decision", null, "CH", "EUR")
        );
        Guid candidate = default;
        Guid criterion = default;
        await fixture.AsAsync(owner, async workspace =>
        {
            await workspace.EditAsync(caseId, 1, evaluation =>
            {
                candidate = evaluation.AddCandidate(new("Example model"));
                criterion = evaluation.AddCriterion(
                    new("Feature", CriterionType.Boolean, Priority.Required, CriterionScope.Vehicle)
                );
            });
            return true;
        });
        await fixture.AsAsync(owner, async workspace =>
        {
            await workspace.ShareAsync(caseId, 2, "reader@example.test", AccessLevel.Reader);
            return true;
        });
        await fixture.AsAsync(owner, async workspace =>
        {
            await workspace.ShareAsync(caseId, 3, "editor@example.test", AccessLevel.Editor);
            return true;
        });

        // A reader may not assess, an editor may.
        await Assert.ThrowsAsync<RuleException>(() =>
            CandidatesAsync(fixture, reader, candidates =>
                candidates.AssessAsync(caseId, 4, candidate, criterion, new(BooleanValue: true))
            )
        );
        await CandidatesAsync(fixture, editor, candidates =>
            candidates.AssessAsync(
                caseId,
                4,
                candidate,
                criterion,
                new(BooleanValue: false, Source: AssessmentSource.TestDrive, Notes: "Observed")
            )
        );

        var saved = await fixture.AsAsync(reader, workspace => workspace.ReadAsync(caseId));
        Assert.Equal(Fulfillment.Unmet, saved.ResultFor(candidate, criterion));
        Assert.Equal("Observed", saved.Assessments.Single().Notes);
        var assessmentId = saved.Assessments.Single().Id;

        // An outdated version number is rejected; the current one updates the same assessment.
        await Assert.ThrowsAsync<RuleException>(() =>
            CandidatesAsync(fixture, editor, candidates =>
                candidates.AssessAsync(caseId, 4, candidate, criterion, new(BooleanValue: true))
            )
        );
        await CandidatesAsync(fixture, editor, candidates =>
            candidates.AssessAsync(caseId, 5, candidate, criterion, new(BooleanValue: true))
        );

        saved = await fixture.AsAsync(reader, workspace => workspace.ReadAsync(caseId));
        Assert.Equal(assessmentId, saved.Assessments.Single().Id);
        Assert.Equal(Fulfillment.Met, saved.ResultFor(candidate, criterion));

        // A revoked editor can no longer change assessments.
        await fixture.AsAsync(owner, async workspace =>
        {
            await workspace.RevokeAsync(caseId, saved.Version, editor);
            return true;
        });
        await Assert.ThrowsAsync<RuleException>(() =>
            CandidatesAsync(fixture, editor, candidates =>
                candidates.ClearAssessmentAsync(caseId, saved.Version + 1, candidate, criterion)
            )
        );
    }

    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Migration_preserves_existing_criteria_and_database_rejects_cross_case_assessments(
        int port
    )
    {
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync();

        // Go back to the first schema, which knows criteria but no cars.
        await fixture.RunAsync(async services =>
        {
            await services
                .GetRequiredService<AppDbContext>()
                .GetService<IMigrator>()
                .MigrateAsync("20261108211927_InitialCommunity");
            return true;
        });

        // Insert an evaluation with one criterion the way that schema stored it, then upgrade.
        var original = new Evaluation(owner, "Existing decision", null);
        var criterion = original.AddCriterion(
            new(
                "Original requirement",
                CriterionType.Boolean,
                Priority.Required,
                CriterionScope.Vehicle
            )
        );
        await fixture.RunAsync(async services =>
        {
            var database = services.GetRequiredService<AppDbContext>();
            await database.Database.ExecuteSqlAsync(
                $"INSERT INTO \"Evaluations\" (\"Id\", \"OwnerId\", \"Name\", \"Purpose\", \"Version\", \"CreatedAt\") VALUES ({original.Id}, {owner}, {original.Name}, '', 1, {original.CreatedAt})"
            );
            await database.Database.ExecuteSqlAsync(
                $"INSERT INTO \"Criterion\" (\"Id\", \"EvaluationId\", \"Name\", \"Type\", \"Priority\", \"Scope\", \"Comparison\", \"Position\") VALUES ({criterion}, {original.Id}, 'Original requirement', 'Boolean', 'Required', 'Vehicle', 'AtLeast', 0)"
            );
            await database.Database.MigrateAsync();
            return true;
        });

        var restored = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(original.Id));
        Assert.Equal(criterion, restored.Criteria.Single().Id);
        Assert.Empty(restored.Candidates);

        // An assessment cannot combine a criterion with the car of another evaluation.
        var other = new Evaluation(owner, "Another decision", null);
        var foreignCandidate = other.AddCandidate(new("Other model"));
        await fixture.RunAsync(async services =>
        {
            var database = services.GetRequiredService<AppDbContext>();
            database.Add(other);
            await database.SaveChangesAsync();
            return true;
        });
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            fixture.RunAsync(async services =>
            {
                await services
                    .GetRequiredService<AppDbContext>()
                    .Database.ExecuteSqlAsync(
                        $"INSERT INTO \"Assessment\" (\"Id\", \"EvaluationId\", \"CandidateId\", \"CriterionId\", \"Source\", \"Notes\") VALUES ({Guid.NewGuid()}, {original.Id}, {foreignCandidate}, {criterion}, 'Unknown', '')"
                    );
                return true;
            })
        );
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }

    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Export_roundtrips_candidates_and_observations_as_independent_records_and_accepts_legacy_files(
        int port
    )
    {
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync();
        var id = await fixture.AsAsync(owner, workspace =>
            workspace.CreateAsync("Decision", null, "CH", "EUR")
        );
        await fixture.AsAsync(owner, async workspace =>
        {
            await workspace.EditAsync(id, 1, AddDeferredCandidateWithRating);
            return true;
        });

        // Export and import as a new evaluation.
        var exported = await fixture.TransferAsync(owner, transfer => transfer.ExportAsync(id));
        var copyId = await fixture.TransferAsync(owner, transfer => transfer.ImportAsync(exported));
        var original = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(id));
        var copy = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(copyId));

        Assert.NotEqual(original.Candidates.Single().Id, copy.Candidates.Single().Id);
        Assert.NotEqual(original.Criteria.Single().Id, copy.Criteria.Single().Id);
        Assert.Equal(CandidateStatus.Deferred, copy.Candidates.Single().Status);
        Assert.Equal("Personal view", copy.Assessments.Single().Notes);
        Assert.Equal(copy.Candidates.Single().Id, copy.Assessments.Single().CandidateId);
        Assert.Equal(copy.Criteria.Single().Id, copy.Assessments.Single().CriterionId);
        Assert.Empty(copy.Shares);

        // An assessment that refers to an unknown car makes the file invalid.
        var invalid = JsonNode.Parse(exported)!;
        invalid["assessments"]![0]!["candidateId"] = Guid.NewGuid();
        await Assert.ThrowsAsync<RuleException>(() =>
            fixture.TransferAsync(owner, transfer =>
                transfer.ImportAsync(Encoding.UTF8.GetBytes(invalid.ToJsonString()))
            )
        );

        // A file of the first format version has no cars at all.
        var legacy =
            """{"format":"steervo-evaluation","version":1,"name":"Legacy decision","purpose":"","groups":[],"criteria":[]}""";
        var legacyId = await fixture.TransferAsync(owner, transfer =>
            transfer.ImportAsync(Encoding.UTF8.GetBytes(legacy))
        );
        var importedLegacy = await fixture.AsAsync(owner, workspace =>
            workspace.ReadAsync(legacyId)
        );
        Assert.Empty(importedLegacy.Candidates);
    }

    private static void AddDeferredCandidateWithRating(Evaluation evaluation)
    {
        var group = evaluation.AddGroup("Group");
        var criterion = evaluation.AddCriterion(
            new(
                "Impression",
                CriterionType.Rating,
                Priority.Required,
                CriterionScope.Vehicle,
                group
            )
        );
        var candidate = evaluation.AddCandidate(
            new("Example model", Links: "https://example.test/specification")
        );
        evaluation.ClassifyCandidate(candidate, CandidateStatus.Deferred, "Waiting for evidence");
        evaluation.AssessCandidate(
            candidate,
            criterion,
            new(RatingValue: 2, Source: AssessmentSource.Inspection, Notes: "Personal view")
        );
    }

    private static Task<bool> CandidatesAsync(
        PostgresFixture fixture,
        string actor,
        Func<CandidateWorkspace, Task> action
    )
    {
        return fixture.AsAsync(actor, async workspace =>
        {
            await action(new CandidateWorkspace(workspace));
            return true;
        });
    }
}
