using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Steervo.Application.Documents;
using Steervo.Application.Offers;
using Steervo.Core;
using Steervo.Infrastructure.Data;
using Steervo.Infrastructure.Documents;
using Xunit;

namespace Steervo.Tests;

[Collection("Postgres")]
public sealed class DocumentPostgresTests
{
    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Creating_and_revising_an_offer_with_attachment_is_atomic(int port)
    {
        // Arrange: an evaluation with one car and an uploaded file that is not attached yet.
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync();
        var evaluationId = await fixture.AsAsync(owner, workspace =>
            workspace.CreateAsync("Attached quote", null, "CH", "CHF")
        );

        Guid car = default;
        await fixture.ActAsync(owner, workspace =>
            workspace.EditAsync(evaluationId, 1, evaluation =>
                car = evaluation.AddCandidate(new("Test car"))
            )
        );

        Task<DocumentFile> SaveAttachmentAsync(string name)
        {
            return fixture.RunAsync(async provider =>
            {
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Synthetic attachment"));
                return await provider
                    .GetRequiredService<IDocumentStorage>()
                    .SaveAsync(stream, name);
            });
        }

        var file = await SaveAttachmentAsync("offer.pdf");

        // An invalid attachment stores neither the offer nor the document.
        await Assert.ThrowsAsync<RuleException>(() =>
            fixture.AsAsync(owner, workspace =>
                new OfferWorkspace(workspace).AddAsync(
                    evaluationId,
                    2,
                    car,
                    new() { Name = "Invalid attachment" },
                    [],
                    "CHF",
                    null,
                    file with
                    {
                        Length = 0,
                    }
                )
            )
        );
        var before = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(evaluationId));
        Assert.Empty(before.Offers);
        Assert.Empty(before.Documents);
        Assert.Equal(2, before.Version);

        // A valid attachment is stored together with the first version of the offer.
        var offer = await fixture.AsAsync(owner, workspace =>
            new OfferWorkspace(workspace).AddAsync(
                evaluationId,
                2,
                car,
                new() { Name = "Quote" },
                [],
                "CHF",
                null,
                file
            )
        );
        var saved = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(evaluationId));
        Assert.Single(saved.Offers);
        Assert.Equal(offer, saved.Documents.Single().OfferId);
        Assert.Equal(1, saved.Documents.Single().Latest.OfferRevisionNumber);
        Assert.Equal(
            "Synthetic attachment",
            await DownloadAsync(fixture, owner, evaluationId, saved.Documents.Single().Id, 1)
        );

        // A revision based on an outdated version stores no second document either.
        var revision = await SaveAttachmentAsync("revised.pdf");
        await Assert.ThrowsAsync<RuleException>(() =>
            fixture.ActAsync(owner, workspace =>
                new OfferWorkspace(workspace).ReviseAsync(
                    evaluationId,
                    2,
                    offer,
                    new() { Name = "Stale" },
                    [],
                    null,
                    revision
                )
            )
        );
        Assert.Single(
            (await fixture.AsAsync(owner, workspace => workspace.ReadAsync(evaluationId))).Documents
        );

        // The valid revision adds the second version and its attachment in one step.
        await fixture.ActAsync(owner, workspace =>
            new OfferWorkspace(workspace).ReviseAsync(
                evaluationId,
                3,
                offer,
                new() { Name = "Updated" },
                [],
                null,
                revision
            )
        );
        saved = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(evaluationId));
        Assert.Equal(2, saved.FindOffer(offer).Latest.Number);
        Assert.Equal(2, saved.Documents.Count);
        Assert.Contains(saved.Documents, document =>
            document.Latest.OfferRevisionNumber == 2
            && document.Latest.File.FileName == "revised.pdf"
        );
    }

    /// <summary>Creates the owner and an evaluation with one car and one offer (version 2).</summary>
    private static async Task<(string Owner, Guid Evaluation, Guid Offer)> SetupAsync(
        PostgresFixture fixture
    )
    {
        var owner = await fixture.SetupAsync();
        var evaluationId = await fixture.AsAsync(owner, workspace =>
            workspace.CreateAsync("Decision", null, "CH", "EUR")
        );

        var offer = Guid.Empty;
        await fixture.ActAsync(owner, workspace =>
            workspace.EditAsync(evaluationId, 1, evaluation =>
                offer = evaluation.AddOffer(
                    evaluation.AddCandidate(new("Vehicle")),
                    new() { Name = "Offer" },
                    [],
                    "EUR"
                )
            )
        );

        return (owner, evaluationId, offer);
    }

    /// <summary>
    /// Uploads the text as "contract.pdf": as a new document, or as a new version when
    /// <paramref name="document"/> names an existing one.
    /// </summary>
    private static Task<bool> UploadAsync(
        PostgresFixture fixture,
        string actor,
        Guid evaluationId,
        long version,
        Guid offer,
        string contents = "first",
        Guid? document = null
    )
    {
        return fixture.DocumentsAsync(actor, async documents =>
        {
            using var input = new MemoryStream(Encoding.UTF8.GetBytes(contents));
            await documents.UploadAsync(
                evaluationId,
                version,
                offer,
                document,
                "Contract",
                input,
                "contract.pdf",
                "Uploaded"
            );
            return true;
        });
    }

    /// <summary>Downloads one version of a document and returns its content as text.</summary>
    private static async Task<string> DownloadAsync(
        PostgresFixture fixture,
        string actor,
        Guid evaluationId,
        Guid document,
        int version
    )
    {
        var result = await fixture.DocumentsAsync(actor, documents =>
            documents.DownloadAsync(evaluationId, document, version)
        );

        await using var stream = result.Content;
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    [Theory, InlineData(55437, false), InlineData(55438, false), InlineData(55438, true)]
    public async Task Files_are_persistent_versioned_authorized_and_deleted_with_the_parent(
        int port,
        bool s3
    )
    {
        // Arrange: an offer in an evaluation shared with a reader and an editor.
        await using var fixture = await PostgresFixture.CreateAsync(port, s3);
        var (owner, evaluationId, offer) = await SetupAsync(fixture);
        var reader = await fixture.InviteAsync(owner, "reader@example.test");
        var editor = await fixture.InviteAsync(owner, "editor@example.test");
        await fixture.ActAsync(owner, async workspace =>
        {
            await workspace.ShareAsync(evaluationId, 2, "reader@example.test", AccessLevel.Reader);
            await workspace.ShareAsync(evaluationId, 3, "editor@example.test", AccessLevel.Editor);
        });

        // Only an editor may upload; a reader may download.
        await Assert.ThrowsAsync<RuleException>(() =>
            UploadAsync(fixture, reader, evaluationId, 4, offer)
        );
        await UploadAsync(fixture, editor, evaluationId, 4, offer);
        var saved = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(evaluationId));
        var document = saved.Documents.Single();
        Assert.Equal("first", await DownloadAsync(fixture, reader, evaluationId, document.Id, 1));

        // A replacement after a new offer version keeps the first file downloadable.
        await fixture.ActAsync(owner, workspace =>
            workspace.EditAsync(evaluationId, 5, evaluation =>
                evaluation.ReviseOffer(offer, new() { Name = "Revised" }, [])
            )
        );
        await UploadAsync(fixture, editor, evaluationId, 6, offer, "second", document.Id);
        saved = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(evaluationId));
        document = saved.Documents.Single();
        Assert.Equal(2, document.Latest.OfferRevisionNumber);
        Assert.Equal("first", await DownloadAsync(fixture, reader, evaluationId, document.Id, 1));
        Assert.Equal("second", await DownloadAsync(fixture, reader, evaluationId, document.Id, 2));

        // The database itself refuses to change a stored version.
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            fixture.ExecuteSqlAsync(
                $"UPDATE \"DocumentRevision\" SET \"Note\" = 'Changed' WHERE \"Id\" = {document.Latest.Id}"
            )
        );
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);

        // An upload based on an outdated version is rejected.
        await Assert.ThrowsAsync<RuleException>(() =>
            UploadAsync(fixture, editor, evaluationId, 6, offer, document: document.Id)
        );

        // No download without a share, and none through another evaluation.
        await fixture.ActAsync(owner, workspace => workspace.RevokeAsync(evaluationId, 7, reader));
        await Assert.ThrowsAsync<RuleException>(() =>
            DownloadAsync(fixture, reader, evaluationId, document.Id, 1)
        );
        var foreign = await fixture.AsAsync(owner, workspace =>
            workspace.CreateAsync("Other", null, "CH", "EUR")
        );
        await Assert.ThrowsAsync<RuleException>(() =>
            DownloadAsync(fixture, owner, foreign, document.Id, 1)
        );

        // Deleting the evaluation removes the versions and the stored files.
        var files = document.Revisions.Select(revision => revision.File).ToArray();
        await fixture.ActAsync(owner, workspace => workspace.DeleteAsync(evaluationId, 8));

        await fixture.ExecuteAsync(async provider =>
        {
            var db = provider.GetRequiredService<AppDbContext>();
            Assert.Empty(await db.Set<DocumentRevision>().ToListAsync());
            Assert.Empty(await db.Set<PendingDocumentDeletion>().ToListAsync());

            foreach (var file in files)
            {
                await Assert.ThrowsAsync<RuleException>(() =>
                    provider
                        .GetRequiredService<IDocumentStorage>()
                        .OpenAsync(file, TestContext.Current.CancellationToken)
                );
            }
        });
    }

    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Archive_preserves_history_as_independent_files_and_rejects_corruption_without_partial_import(
        int port
    )
    {
        // Arrange: a document with two versions.
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var (owner, evaluationId, offer) = await SetupAsync(fixture);
        await UploadAsync(fixture, owner, evaluationId, 2, offer);
        var original = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(evaluationId));
        var document = original.Documents.Single();
        await UploadAsync(fixture, owner, evaluationId, 3, offer, "second", document.Id);

        // Act: export the evaluation as an archive and import it as a copy.
        var export = await fixture.ArchiveAsync(owner, archives =>
            archives.ExportAsync(evaluationId)
        );
        await using var archive = export.Content;
        Assert.Equal("application/zip", export.ContentType);
        var copyId = await fixture.ArchiveAsync(owner, archives => archives.ImportAsync(archive));

        // Assert: the copy has the whole history under ids and files of its own.
        var copy = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(copyId));
        var copied = copy.Documents.Single();
        Assert.Equal("CH", copy.Country);
        Assert.Equal("EUR", copy.Currency);
        Assert.Equal(2, copied.Revisions.Count);
        Assert.NotEqual(document.Id, copied.Id);
        Assert.NotEqual(document.Latest.File.ContentId, copied.Revisions[0].File.ContentId);
        Assert.Empty(copy.Shares);
        Assert.Equal("first", await DownloadAsync(fixture, owner, copyId, copied.Id, 1));

        // The copied files survive the deletion of the original.
        await fixture.ActAsync(owner, workspace => workspace.DeleteAsync(evaluationId, 4));
        Assert.Equal("second", await DownloadAsync(fixture, owner, copyId, copied.Id, 2));

        // Rewrite the archive with a mismatched digest; no evaluation may be created.
        using var corrupted = await CopyWithWrongDigestAsync(archive);
        await Assert.ThrowsAsync<RuleException>(() =>
            fixture.ArchiveAsync(owner, archives => archives.ImportAsync(corrupted))
        );
        Assert.Single(await fixture.AsAsync(owner, workspace => workspace.ListAsync()));
    }

    /// <summary>
    /// Copies the archive entry by entry and replaces the digest of the first document version in
    /// the manifest, so that it no longer matches the file.
    /// </summary>
    private static async Task<MemoryStream> CopyWithWrongDigestAsync(Stream archive)
    {
        archive.Position = 0;
        var copy = new MemoryStream();

        using (var source = new ZipArchive(archive, ZipArchiveMode.Read, true))
        using (var target = new ZipArchive(copy, ZipArchiveMode.Create, true))
        {
            foreach (var entry in source.Entries)
            {
                await using var input = entry.Open();
                await using var output = target.CreateEntry(entry.FullName).Open();

                if (entry.FullName == "documents.json")
                {
                    var manifest = await JsonNode.ParseAsync(input);
                    manifest!["documents"]![0]!["revisions"]![0]!["sha256"] = new string('0', 64);
                    await output.WriteAsync(Encoding.UTF8.GetBytes(manifest.ToJsonString()));
                }
                else
                {
                    await input.CopyToAsync(output);
                }
            }
        }

        copy.Position = 0;
        return copy;
    }

    [Fact]
    public async Task Interrupted_and_expired_uploads_are_retried_safely_without_deleting_live_files()
    {
        // Arrange: one attached document and one saved file that was never attached.
        await using var fixture = await PostgresFixture.CreateAsync(55438);
        var (owner, evaluationId, offer) = await SetupAsync(fixture);
        await UploadAsync(fixture, owner, evaluationId, 2, offer);
        var saved = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(evaluationId));
        var live = saved.Documents.Single();
        var abandoned = await fixture.RunAsync(async provider =>
        {
            using var data = new MemoryStream("abandoned"u8.ToArray());
            return await provider
                .GetRequiredService<IDocumentStorage>()
                .SaveAsync(data, "abandoned.pdf");
        });

        // Make the abandoned file due for deletion.
        await fixture.ExecuteSqlAsync(
            $"UPDATE \"PendingDocumentDeletion\" SET \"AvailableAt\" = CURRENT_TIMESTAMP - INTERVAL '1 hour' WHERE \"Id\" = {abandoned.ContentId}"
        );

        // An expired file can no longer be attached.
        await Assert.ThrowsAsync<RuleException>(() =>
            fixture.ActAsync(owner, workspace =>
                workspace.EditAsync(evaluationId, 3, evaluation =>
                    evaluation.AddDocument(offer, "Expired", abandoned)
                )
            )
        );

        // A cleanup that fails or runs out of time removes nothing and keeps the file queued.
        await fixture.ExecuteAsync(async provider =>
        {
            var db = provider.GetRequiredService<AppDbContext>();

            Task<int> CleanUpWithAsync(IDocumentObjects objects)
            {
                return new DocumentCleanup(
                    db,
                    objects,
                    NullLogger<DocumentCleanup>.Instance
                ).RunAsync();
            }

            Assert.Equal(0, await CleanUpWithAsync(new UnavailableObjects()));
            Assert.Equal(0, await CleanUpWithAsync(new SlowObjects()));
            Assert.Single(await db.Set<PendingDocumentDeletion>().ToListAsync());
        });

        // The next regular cleanup removes the abandoned file and leaves the live one alone.
        await fixture.ExecuteAsync(async provider =>
        {
            Assert.Equal(1, await provider.GetRequiredService<DocumentCleanup>().RunAsync());
        });
        Assert.Equal("first", await DownloadAsync(fixture, owner, evaluationId, live.Id, 1));
        await fixture.ExecuteAsync(provider =>
            Assert.ThrowsAsync<RuleException>(() =>
                provider
                    .GetRequiredService<IDocumentStorage>()
                    .OpenAsync(abandoned, TestContext.Current.CancellationToken)
            )
        );
    }

    [Theory, InlineData(55437), InlineData(55438)]
    public async Task Upgrade_preserves_existing_offers_and_currency(int port)
    {
        // Arrange: an offer with two versions and a second offer without lines.
        await using var fixture = await PostgresFixture.CreateAsync(port);
        var owner = await fixture.SetupAsync();
        var evaluation = new Evaluation(owner, "Existing", null);
        var candidate = evaluation.AddCandidate(new("Existing vehicle"));
        var offerId = evaluation.AddOffer(
            candidate,
            new() { Name = "Existing offer", StatedTotal = 23456 },
            [
                new("Mats", PriceKind: OfferPriceKind.Included),
                new("Delivery", OfferLineKind.Fee, OfferPriceKind.Amount, 500),
            ],
            "EUR"
        );
        evaluation.ReviseOffer(
            offerId,
            new()
            {
                Name = "Completed offer",
                StatedTotal = 23000,
                VehicleDescription = "Existing vehicle Touring",
            },
            [],
            "Updated"
        );
        var other = evaluation.AddOffer(
            candidate,
            new() { Name = "Newest offer" },
            [],
            createdAt: null
        );

        // Act: store it, go back to the schema that introduced offers, then forward again.
        await fixture.ExecuteAsync(async provider =>
        {
            var db = provider.GetRequiredService<AppDbContext>();

            db.Evaluations.Add(evaluation);
            await db.SaveChangesAsync();
            await db.GetService<IMigrator>().MigrateAsync("20261110184627_OffersAndRevisions");
            await db.Database.MigrateAsync();
        });

        // Assert
        var saved = await fixture.AsAsync(owner, workspace => workspace.ReadAsync(evaluation.Id));
        Assert.Equal("EUR", saved.Currency);
        Assert.Empty(saved.Documents);

        // The former base price leads each version as the vehicle line; the other lines keep their order.
        var first = saved.FindOffer(offerId).Revision(1);
        var latest = saved.FindOffer(offerId).Latest;
        Assert.Equal(
            ["Fahrzeug", "Mats", "Delivery"],
            first.Lines.OrderBy(line => line.Position).Select(line => line.Name)
        );
        Assert.Equal([0, 1, 2], first.Lines.Select(line => line.Position).Order());
        Assert.Equal(23956, first.Pricing.Total);
        Assert.Null(first.Terms.StatedTotal);
        Assert.Equal("Existing vehicle Touring", latest.Lines.Single().Name);
        Assert.Equal(OfferLineKind.Vehicle, latest.Lines.Single().Kind);
        Assert.Equal(23000, latest.Pricing.Total);
        Assert.True(latest.Lines.Single().Financed);
        Assert.Empty(saved.FindOffer(other).Latest.Lines);
        Assert.Equal([0, 1], saved.Offers.Select(offer => offer.Position).Order());

        // Versions stay protected after the conversion.
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            fixture.ExecuteSqlAsync(
                $"UPDATE \"OfferLine\" SET \"Name\" = 'Changed' WHERE \"Id\" = {latest.Lines.Single().Id}"
            )
        );
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    /// <summary>An object store that is down: every operation fails at once.</summary>
    private sealed class UnavailableObjects : IDocumentObjects
    {
        public Task PutAsync(Guid id, Stream input, CancellationToken cancellationToken)
        {
            throw new IOException();
        }

        public Task CopyToAsync(Guid id, Stream output, CancellationToken cancellationToken)
        {
            throw new IOException();
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            throw new IOException();
        }
    }

    /// <summary>An object store whose deletions never finish until they are cancelled.</summary>
    private sealed class SlowObjects : IDocumentObjects
    {
        public Task PutAsync(Guid id, Stream input, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task CopyToAsync(Guid id, Stream output, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }
}
