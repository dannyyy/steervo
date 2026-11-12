using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Steervo.Application.Documents;
using Steervo.Application.Evaluations;
using Steervo.Core;
using Steervo.Infrastructure.Data;
using Steervo.Infrastructure.Documents;
using Xunit;

namespace Steervo.Tests;

[Collection("Postgres")]
public sealed class DocumentPostgresTests
{
    private static async Task<(string Owner, Guid Evaluation, Guid Offer)> SetupAsync(PostgresFixture fixture)
    {
        var owner = await fixture.SetupAsync(); var id = await fixture.AsAsync(owner, w => w.CreateAsync("Decision", null)); var offer = Guid.Empty;
        await fixture.AsAsync(owner, async w => { await w.EditAsync(id, 1, e => offer = e.AddOffer(e.AddCandidate(new("Vehicle")), new() { Name = "Offer" }, [], "EUR")); return true; });
        return (owner, id, offer);
    }
    private static Task<bool> UploadAsync(PostgresFixture fixture, string actor, Guid id, long version, Guid offer, string contents = "first", Guid? document = null) =>
        fixture.DocumentsAsync(actor, async w => { using var input = new MemoryStream(Encoding.UTF8.GetBytes(contents)); await w.UploadAsync(id, version, offer, document, "Contract", input, "contract.pdf", "Uploaded"); return true; });
    private static async Task<string> DownloadAsync(PostgresFixture fixture, string actor, Guid id, Guid document, int version)
    {
        var result = await fixture.DocumentsAsync(actor, w => w.DownloadAsync(id, document, version));
        await using var stream = result.Content; using var reader = new StreamReader(stream); return await reader.ReadToEndAsync();
    }
    [Theory, InlineData(55437, false), InlineData(55438, false), InlineData(55438, true)]
    public async Task Files_are_persistent_versioned_authorized_and_deleted_with_the_parent(int port, bool s3)
    {
        await using var fixture = await PostgresFixture.CreateAsync(port, s3); var (owner, id, offer) = await SetupAsync(fixture);
        var reader = await fixture.InviteAsync(owner, "reader@example.test"); var editor = await fixture.InviteAsync(owner, "editor@example.test");
        await fixture.AsAsync(owner, async w => { await w.ShareAsync(id, 2, "reader@example.test", AccessLevel.Reader); await w.ShareAsync(id, 3, "editor@example.test", AccessLevel.Editor); return true; });
        await Assert.ThrowsAsync<RuleException>(() => UploadAsync(fixture, reader, id, 4, offer));
        await UploadAsync(fixture, editor, id, 4, offer);
        var saved = await fixture.AsAsync(owner, w => w.ReadAsync(id)); var doc = saved.Documents.Single();
        Assert.Equal("first", await DownloadAsync(fixture, reader, id, doc.Id, 1));
        await fixture.AsAsync(owner, async w => { await w.EditAsync(id, 5, e => e.ReviseOffer(offer, new() { Name = "Revised" }, [])); return true; });
        await UploadAsync(fixture, editor, id, 6, offer, "second", doc.Id);
        saved = await fixture.AsAsync(owner, w => w.ReadAsync(id)); doc = saved.Documents.Single();
        Assert.Equal(2, doc.Latest.OfferRevisionNumber); Assert.Equal("first", await DownloadAsync(fixture, reader, id, doc.Id, 1));
        Assert.Equal("second", await DownloadAsync(fixture, reader, id, doc.Id, 2));
        var error = await Assert.ThrowsAsync<PostgresException>(() => fixture.RunAsync(async p => {
            await p.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync($"UPDATE \"DocumentRevision\" SET \"Note\" = 'Changed' WHERE \"Id\" = {doc.Latest.Id}"); return true;
        }));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        await Assert.ThrowsAsync<RuleException>(() => UploadAsync(fixture, editor, id, 6, offer, document: doc.Id));
        await fixture.AsAsync(owner, async w => { await w.RevokeAsync(id, 7, reader); return true; });
        await Assert.ThrowsAsync<RuleException>(() => DownloadAsync(fixture, reader, id, doc.Id, 1));
        var foreign = await fixture.AsAsync(owner, w => w.CreateAsync("Other", null));
        await Assert.ThrowsAsync<RuleException>(() => DownloadAsync(fixture, owner, foreign, doc.Id, 1));
        var content = doc.Revisions.Select(r => r.File).ToArray();
        await fixture.AsAsync(owner, async w => { await w.DeleteAsync(id, 8); return true; });
        await fixture.RunAsync(async p => {
            var db = p.GetRequiredService<AppDbContext>(); Assert.Empty(await db.Set<DocumentRevision>().ToListAsync());
            Assert.Empty(await db.Set<PendingDocumentDeletion>().ToListAsync());
            foreach (var file in content) await Assert.ThrowsAsync<RuleException>(() => p.GetRequiredService<IDocumentStorage>().OpenAsync(file));
            return true;
        });
    }
    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Archive_preserves_history_as_independent_files_and_rejects_corruption_without_partial_import(int port)
    {
        await using var fixture = await PostgresFixture.CreateAsync(port); var (owner, id, offer) = await SetupAsync(fixture);
        await UploadAsync(fixture, owner, id, 2, offer);
        var original = await fixture.AsAsync(owner, w => w.ReadAsync(id)); var doc = original.Documents.Single();
        await UploadAsync(fixture, owner, id, 3, offer, "second", doc.Id);
        var export = await fixture.ArchiveAsync(owner, a => a.ExportAsync(id));
        await using var archive = export.Content; Assert.Equal("application/zip", export.ContentType);
        var copyId = await fixture.ArchiveAsync(owner, a => a.ImportAsync(archive));
        var copy = await fixture.AsAsync(owner, w => w.ReadAsync(copyId)); var copied = copy.Documents.Single();
        Assert.Equal(2, copied.Revisions.Count); Assert.NotEqual(doc.Id, copied.Id); Assert.NotEqual(doc.Latest.File.ContentId, copied.Revisions[0].File.ContentId);
        Assert.Empty(copy.Shares); Assert.Equal("first", await DownloadAsync(fixture, owner, copyId, copied.Id, 1));
        await fixture.AsAsync(owner, async w => { await w.DeleteAsync(id, 4); return true; });
        Assert.Equal("second", await DownloadAsync(fixture, owner, copyId, copied.Id, 2));
        // Rewrite the archive with a mismatched digest; no evaluation may be created.
        archive.Position = 0; using var bad = new MemoryStream();
        using (var source = new ZipArchive(archive, ZipArchiveMode.Read, true))
        using (var target = new ZipArchive(bad, ZipArchiveMode.Create, true))
        {
            foreach (var entry in source.Entries)
            {
                await using var input = entry.Open(); await using var output = target.CreateEntry(entry.FullName).Open();
                if (entry.FullName == "documents.json")
                {
                    var manifest = await JsonNode.ParseAsync(input); manifest!["documents"]![0]!["revisions"]![0]!["sha256"] = new string('0', 64);
                    await output.WriteAsync(Encoding.UTF8.GetBytes(manifest.ToJsonString()));
                }
                else await input.CopyToAsync(output);
            }
        }
        bad.Position = 0;
        await Assert.ThrowsAsync<RuleException>(() => fixture.ArchiveAsync(owner, a => a.ImportAsync(bad)));
        Assert.Single(await fixture.AsAsync(owner, w => w.ListAsync()));
    }
    [Fact]
    public async Task Interrupted_and_expired_uploads_are_retried_safely_without_deleting_live_files()
    {
        await using var fixture = await PostgresFixture.CreateAsync(55438); var (owner, id, offer) = await SetupAsync(fixture);
        await UploadAsync(fixture, owner, id, 2, offer);
        var saved = await fixture.AsAsync(owner, w => w.ReadAsync(id)); var live = saved.Documents.Single();
        var abandoned = await fixture.RunAsync(async p => {
            using var data = new MemoryStream("abandoned"u8.ToArray());
            return await p.GetRequiredService<IDocumentStorage>().SaveAsync(data, "abandoned.pdf");
        });
        await fixture.RunAsync(async p => {
            var db = p.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"PendingDocumentDeletion\" SET \"AvailableAt\" = CURRENT_TIMESTAMP - INTERVAL '1 hour' WHERE \"Id\" = {abandoned.ContentId}");
            return true;
        });
        await Assert.ThrowsAsync<RuleException>(() => fixture.AsAsync(owner, async w => { await w.EditAsync(id, 3, e => e.AddDocument(offer, "Expired", abandoned)); return true; }));
        await fixture.RunAsync(async p => {
            var db = p.GetRequiredService<AppDbContext>();
            Assert.Equal(0, await new DocumentCleanup(db, new UnavailableObjects(), NullLogger<DocumentCleanup>.Instance).RunAsync());
            Assert.Equal(0, await new DocumentCleanup(db, new SlowObjects(), NullLogger<DocumentCleanup>.Instance).RunAsync());
            Assert.Single(await db.Set<PendingDocumentDeletion>().ToListAsync()); return true;
        });
        await fixture.RunAsync(async p => { Assert.Equal(1, await p.GetRequiredService<DocumentCleanup>().RunAsync()); return true; });
        Assert.Equal("first", await DownloadAsync(fixture, owner, id, live.Id, 1));
        await fixture.RunAsync(async p => { await Assert.ThrowsAsync<RuleException>(() => p.GetRequiredService<IDocumentStorage>().OpenAsync(abandoned)); return true; });
    }
    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Upgrade_preserves_existing_offers_and_currency(int port)
    {
        await using var fixture = await PostgresFixture.CreateAsync(port); var owner = await fixture.SetupAsync();
        var evaluation = new Evaluation(owner, "Existing", null); var candidate = evaluation.AddCandidate(new("Existing vehicle"));
        var offerId = evaluation.AddOffer(candidate, new() { Name = "Existing offer", BasePrice = 23456 }, [], "EUR");
        evaluation.ReviseOffer(offerId, new() { Name = "Completed offer", BasePrice = 23000 }, [], "Updated");
        await fixture.RunAsync(async p => {
            var db = p.GetRequiredService<AppDbContext>();
            await db.GetService<IMigrator>().MigrateAsync("20261110184627_OffersAndRevisions");
            db.Evaluations.Add(evaluation); await db.SaveChangesAsync();
            await db.Database.MigrateAsync(); return true;
        });
        var saved = await fixture.AsAsync(owner, w => w.ReadAsync(evaluation.Id));
        Assert.Equal("EUR", saved.Currency); Assert.Empty(saved.Documents);
        Assert.Equal(23456, saved.FindOffer(offerId).Revision(1).Terms.BasePrice);
        Assert.Equal(23000, saved.FindOffer(offerId).Latest.Terms.BasePrice);
    }
    private sealed class UnavailableObjects : IDocumentObjects
    {
        public Task PutAsync(Guid id, Stream input, CancellationToken cancellationToken) => throw new IOException();
        public Task CopyToAsync(Guid id, Stream output, CancellationToken cancellationToken) => throw new IOException();
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new IOException();
    }
    private sealed class SlowObjects : IDocumentObjects
    {
        public Task PutAsync(Guid id, Stream input, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CopyToAsync(Guid id, Stream output, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => Task.Delay(Timeout.Infinite, cancellationToken);
    }
}
