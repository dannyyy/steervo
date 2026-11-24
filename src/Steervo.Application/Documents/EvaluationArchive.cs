using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Steervo.Application.Evaluations;
using Steervo.Core;

namespace Steervo.Application.Documents;

public sealed record DocumentVersionExport(
    int Number,
    int OfferRevisionNumber,
    string FileName,
    long Length,
    string Sha256,
    string Entry,
    string Note,
    DateTimeOffset CreatedAt
);

public sealed record DocumentExport(Guid OfferId, string Name, DocumentVersionExport[] Revisions);

public sealed record DocumentManifest(string Format, int Version, DocumentExport[] Documents);

public sealed record EvaluationDownload(Stream Content, string ContentType, string FileName);

/// <summary>
/// Exports an evaluation together with its documents as a ZIP archive and imports such archives
/// as well as plain JSON exports.
/// </summary>
public sealed class EvaluationArchive(
    EvaluationWorkspace workspace,
    EvaluationTransfer transfer,
    IEvaluationStore store,
    IDocumentStorage storage
)
{
    public const long MaximumBytes = 250 * 1024 * 1024;

    private const string EvaluationEntry = "evaluation.json";
    private const string ManifestEntry = "documents.json";
    private const int MetadataEntries = 2;

    // A document file is stored as "files/" followed by a GUID of 32 hexadecimal digits.
    private const string FileEntryPrefix = "files/";
    private const string FileEntryIdFormat = "N";
    private const int FileEntryIdLength = 32;

    private const string ManifestFormat = "steervo-documents";
    private const int ManifestVersion = 1;
    private const int MaximumDocuments = 1000;
    private const int MaximumRevisionsPerDocument = 100;

    private const string InvalidImport = "InvalidImport";
    private const string ExportTooLarge = "ExportTooLarge";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>
    /// Returns plain JSON for an evaluation without documents, otherwise a ZIP archive holding
    /// the JSON, every document file and a manifest describing the files.
    /// </summary>
    public async Task<EvaluationDownload> ExportAsync(
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        var evaluation = await workspace.ReadAsync(id);
        var data = EvaluationTransfer.Serialize(evaluation);

        if (data.Length > EvaluationTransfer.MaximumBytes)
        {
            throw new RuleException(ExportTooLarge);
        }

        if (evaluation.Documents.Count == 0)
        {
            return new(new MemoryStream(data), "application/json", $"evaluation-{id}.json");
        }

        var output = DocumentStreams.Temporary();

        try
        {
            // The archive must be disposed before the stream is read: that writes its directory.
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                await WriteEntryAsync(archive, EvaluationEntry, data, cancellationToken);

                var documents = new List<DocumentExport>();

                foreach (var document in evaluation.Documents)
                {
                    var revisions = new List<DocumentVersionExport>();

                    foreach (var revision in document.Revisions.OrderBy(r => r.Number))
                    {
                        revisions.Add(await AddFileAsync(archive, revision, cancellationToken));
                    }

                    documents.Add(new(document.OfferId, document.Name, revisions.ToArray()));
                }

                var manifest = JsonSerializer.SerializeToUtf8Bytes(
                    new DocumentManifest(ManifestFormat, ManifestVersion, documents.ToArray()),
                    Json
                );

                if (manifest.Length > EvaluationTransfer.MaximumBytes)
                {
                    throw new RuleException(ExportTooLarge);
                }

                await WriteEntryAsync(archive, ManifestEntry, manifest, cancellationToken);
            }

            output.Position = 0;

            return new(output, "application/zip", $"evaluation-{id}.zip");
        }
        catch
        {
            // The caller only owns the temporary file once it has been returned.
            await output.DisposeAsync();
            throw;
        }
    }

    public async Task<Guid> ImportAsync(Stream input, CancellationToken cancellationToken = default)
    {
        await using var buffer = DocumentStreams.Temporary();
        await DocumentStreams.CopyAsync(input, buffer, MaximumBytes, cancellationToken);

        buffer.Position = 0;
        var isArchive = StartsWithZipSignature(buffer);
        buffer.Position = 0;

        if (!isArchive)
        {
            return await ImportJsonAsync(buffer, cancellationToken);
        }

        try
        {
            return await ImportArchiveAsync(buffer, cancellationToken);
        }
        catch (Exception error)
            when (error is InvalidDataException or JsonException or OverflowException)
        {
            throw new RuleException(InvalidImport);
        }
    }

    private static async Task WriteEntryAsync(
        ZipArchive archive,
        string name,
        byte[] content,
        CancellationToken cancellationToken
    )
    {
        await using var entry = archive.CreateEntry(name, CompressionLevel.NoCompression).Open();

        await entry.WriteAsync(content, cancellationToken);
    }

    /// <summary>
    /// Copies one stored file into the archive under a new, unguessable entry name.
    /// </summary>
    private async Task<DocumentVersionExport> AddFileAsync(
        ZipArchive archive,
        DocumentRevision revision,
        CancellationToken cancellationToken
    )
    {
        var name = FileEntryPrefix + Guid.NewGuid().ToString(FileEntryIdFormat);

        await using var input = await storage.OpenAsync(revision.File, cancellationToken);
        await using var entry = archive.CreateEntry(name, CompressionLevel.NoCompression).Open();
        await input.CopyToAsync(entry, cancellationToken);

        return new(
            revision.Number,
            revision.OfferRevisionNumber,
            revision.File.FileName,
            revision.File.Length,
            revision.File.Sha256,
            name,
            revision.Note,
            revision.CreatedAt
        );
    }

    private static bool StartsWithZipSignature(Stream stream)
    {
        return stream.ReadByte() == 'P' && stream.ReadByte() == 'K';
    }

    private async Task<Guid> ImportJsonAsync(Stream buffer, CancellationToken cancellationToken)
    {
        if (buffer.Length > EvaluationTransfer.MaximumBytes)
        {
            throw new RuleException(InvalidImport);
        }

        using var json = new MemoryStream();
        await buffer.CopyToAsync(json, cancellationToken);

        return await transfer.ImportAsync(json.ToArray());
    }

    private async Task<Guid> ImportArchiveAsync(Stream buffer, CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: true);
        RequirePlausibleEntries(archive);

        var prepared = transfer.PrepareImport(
            await ReadMetadataAsync(archive.GetEntry(EvaluationEntry), cancellationToken)
        );
        var manifest = await ReadManifestAsync(archive, cancellationToken);

        // Validate every reference and version before writing any object.
        ValidateDocuments(prepared.Evaluation, prepared.OfferIds, manifest, archive);

        // Validation restored the documents with placeholder files; start again with stored ones.
        prepared.Evaluation.Documents.Clear();
        await StoreDocumentsAsync(
            prepared.Evaluation,
            prepared.OfferIds,
            manifest,
            archive,
            cancellationToken
        );

        await store.CreateAsync(prepared.Evaluation, cancellationToken);

        return prepared.Evaluation.Id;
    }

    private static void RequirePlausibleEntries(ZipArchive archive)
    {
        var entryCount = archive.Entries.Count;

        if (entryCount > Evaluation.MaximumDocumentRevisions + MetadataEntries)
        {
            throw new RuleException(InvalidImport);
        }

        var distinctNames = archive
            .Entries.Select(entry => entry.FullName)
            .Distinct(StringComparer.Ordinal)
            .Count();

        if (distinctNames != archive.Entries.Count)
        {
            throw new RuleException(InvalidImport);
        }
    }

    private static async Task<DocumentManifest> ReadManifestAsync(
        ZipArchive archive,
        CancellationToken cancellationToken
    )
    {
        var manifest = JsonSerializer.Deserialize<DocumentManifest>(
            await ReadMetadataAsync(archive.GetEntry(ManifestEntry), cancellationToken),
            Json
        );

        if (
            manifest?.Format != ManifestFormat
            || manifest.Version != ManifestVersion
            || manifest.Documents is null
            || manifest.Documents.Length > MaximumDocuments
            || manifest.Documents.Any(HasNoUsableRevisions)
        )
        {
            throw new RuleException(InvalidImport);
        }

        RequireValidVersions(manifest, archive);

        return manifest;
    }

    private static bool HasNoUsableRevisions(DocumentExport document)
    {
        return document is null
            || document.Revisions is null
            || document.Revisions.Length is < 1 or > MaximumRevisionsPerDocument;
    }

    /// <summary>
    /// Every version must name its own file entry, the archive must hold nothing else, and the
    /// stated sizes must stay within the limits for documents.
    /// </summary>
    private static void RequireValidVersions(DocumentManifest manifest, ZipArchive archive)
    {
        var versions = manifest.Documents.SelectMany(document => document.Revisions).ToArray();

        if (
            versions.Length > Evaluation.MaximumDocumentRevisions
            || versions.Any(version => version is null || !IsFileEntryName(version.Entry))
        )
        {
            throw new RuleException(InvalidImport);
        }

        var distinctEntries = versions
            .Select(version => version.Entry)
            .Distinct(StringComparer.Ordinal)
            .Count();

        if (
            distinctEntries != versions.Length
            || archive.Entries.Count != versions.Length + MetadataEntries
        )
        {
            throw new RuleException(InvalidImport);
        }

        if (
            versions.Any(version => version.Length is < 1 or > DocumentFile.MaximumBytes)
            || versions.Sum(version => version.Length) > Evaluation.MaximumDocumentBytes
        )
        {
            throw new RuleException(InvalidImport);
        }
    }

    private static bool IsFileEntryName(string? name)
    {
        return name is not null
            && name.Length == FileEntryPrefix.Length + FileEntryIdLength
            && name.StartsWith(FileEntryPrefix, StringComparison.Ordinal)
            && Guid.TryParseExact(name[FileEntryPrefix.Length..], FileEntryIdFormat, out _);
    }

    /// <summary>
    /// Restores every document with placeholder files so that all rules for documents are
    /// checked, and compares the stated file sizes with the archive, before any file is stored.
    /// </summary>
    private static void ValidateDocuments(
        Evaluation evaluation,
        Dictionary<Guid, Guid> offerIds,
        DocumentManifest manifest,
        ZipArchive archive
    )
    {
        foreach (var document in manifest.Documents)
        {
            if (!offerIds.TryGetValue(document.OfferId, out var offerId))
            {
                throw new RuleException(InvalidImport);
            }

            var isNumberedWithoutGaps = document
                .Revisions.Select(version => version.Number)
                .SequenceEqual(Enumerable.Range(1, document.Revisions.Length));

            if (!isNumberedWithoutGaps)
            {
                throw new RuleException(InvalidImport);
            }

            var placeholderFiles = document.Revisions.Select(version =>
                (
                    version.OfferRevisionNumber,
                    new DocumentFile(
                        Guid.NewGuid(),
                        version.FileName,
                        version.Length,
                        version.Sha256
                    ),
                    (string?)version.Note,
                    version.CreatedAt
                )
            );

            evaluation.RestoreDocument(offerId, document.Name, placeholderFiles);

            foreach (var version in document.Revisions)
            {
                if (archive.GetEntry(version.Entry)?.Length != version.Length)
                {
                    throw new RuleException(InvalidImport);
                }
            }
        }
    }

    private async Task StoreDocumentsAsync(
        Evaluation evaluation,
        Dictionary<Guid, Guid> offerIds,
        DocumentManifest manifest,
        ZipArchive archive,
        CancellationToken cancellationToken
    )
    {
        foreach (var document in manifest.Documents)
        {
            var files = new List<(int, DocumentFile, string?, DateTimeOffset)>();

            foreach (var version in document.Revisions)
            {
                await using var source = archive.GetEntry(version.Entry)!.Open();
                var file = await storage.SaveAsync(source, version.FileName, cancellationToken);

                // The stored content must be exactly what the manifest describes.
                if (
                    file.Length != version.Length
                    || file.Sha256 != version.Sha256.ToLowerInvariant()
                )
                {
                    throw new RuleException(InvalidImport);
                }

                files.Add((version.OfferRevisionNumber, file, version.Note, version.CreatedAt));
            }

            evaluation.RestoreDocument(offerIds[document.OfferId], document.Name, files);
        }
    }

    private static async Task<byte[]> ReadMetadataAsync(
        ZipArchiveEntry? entry,
        CancellationToken cancellationToken
    )
    {
        if (entry is null || entry.Length > EvaluationTransfer.MaximumBytes)
        {
            throw new RuleException(InvalidImport);
        }

        await using var input = entry.Open();
        using var output = new MemoryStream();
        await DocumentStreams.CopyAsync(
            input,
            output,
            EvaluationTransfer.MaximumBytes,
            cancellationToken
        );

        return output.ToArray();
    }
}
