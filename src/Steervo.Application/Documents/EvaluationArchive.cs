using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Steervo.Application.Evaluations;
using Steervo.Core;

namespace Steervo.Application.Documents;

public sealed record DocumentVersionExport(int Number, int OfferRevisionNumber, string FileName, long Length, string Sha256, string Entry, string Note, DateTimeOffset CreatedAt);
public sealed record DocumentExport(Guid OfferId, string Name, DocumentVersionExport[] Revisions);
public sealed record DocumentManifest(string Format, int Version, DocumentExport[] Documents);
public sealed record EvaluationDownload(Stream Content, string ContentType, string FileName);

public sealed class EvaluationArchive(EvaluationWorkspace workspace, EvaluationTransfer transfer, IEvaluationStore store, IDocumentStorage storage)
{
    public const long MaximumBytes = 250 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) {
        WriteIndented = true, MaxDepth = 16, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    public async Task<EvaluationDownload> ExportAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var evaluation = await workspace.ReadAsync(id);
        var data = EvaluationTransfer.Serialize(evaluation);
        if (data.Length > EvaluationTransfer.MaximumBytes) throw new RuleException("ExportTooLarge");
        if (evaluation.Documents.Count == 0) return new(new MemoryStream(data), "application/json", $"evaluation-{id}.json");
        var output = DocumentStreams.Temporary();
        try
        {
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                await using (var entry = archive.CreateEntry("evaluation.json", CompressionLevel.NoCompression).Open()) await entry.WriteAsync(data, cancellationToken);
                var documents = new List<DocumentExport>();
                foreach (var document in evaluation.Documents)
                {
                    var revisions = new List<DocumentVersionExport>();
                    foreach (var revision in document.Revisions.OrderBy(r => r.Number))
                    {
                        var name = "files/" + Guid.NewGuid().ToString("N");
                        await using var input = await storage.OpenAsync(revision.File, cancellationToken);
                        await using var entry = archive.CreateEntry(name, CompressionLevel.NoCompression).Open();
                        await input.CopyToAsync(entry, cancellationToken);
                        revisions.Add(new(revision.Number, revision.OfferRevisionNumber, revision.File.FileName, revision.File.Length, revision.File.Sha256, name, revision.Note, revision.CreatedAt));
                    }
                    documents.Add(new(document.OfferId, document.Name, revisions.ToArray()));
                }
                var manifest = JsonSerializer.SerializeToUtf8Bytes(new DocumentManifest("steervo-documents", 1, documents.ToArray()), Json);
                if (manifest.Length > EvaluationTransfer.MaximumBytes) throw new RuleException("ExportTooLarge");
                await using var metadata = archive.CreateEntry("documents.json", CompressionLevel.NoCompression).Open();
                await metadata.WriteAsync(manifest, cancellationToken);
            }
            output.Position = 0;
            return new(output, "application/zip", $"evaluation-{id}.zip");
        }
        catch { await output.DisposeAsync(); throw; }
    }
    public async Task<Guid> ImportAsync(Stream input, CancellationToken cancellationToken = default)
    {
        await using var buffer = DocumentStreams.Temporary();
        await DocumentStreams.CopyAsync(input, buffer, MaximumBytes, cancellationToken);
        buffer.Position = 0;
        if (buffer.ReadByte() != 'P' || buffer.ReadByte() != 'K')
        {
            buffer.Position = 0;
            if (buffer.Length > EvaluationTransfer.MaximumBytes) throw new RuleException("InvalidImport");
            using var json = new MemoryStream(); await buffer.CopyToAsync(json, cancellationToken);
            return await transfer.ImportAsync(json.ToArray());
        }
        buffer.Position = 0;
        try
        {
            using var archive = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count > Evaluation.MaximumDocumentRevisions + 2 || archive.Entries.Select(e => e.FullName).Distinct(StringComparer.Ordinal).Count() != archive.Entries.Count)
                throw new RuleException("InvalidImport");
            var prepared = transfer.PrepareImport(await ReadMetadataAsync(archive.GetEntry("evaluation.json"), cancellationToken));
            var manifest = JsonSerializer.Deserialize<DocumentManifest>(await ReadMetadataAsync(archive.GetEntry("documents.json"), cancellationToken), Json);
            if (manifest?.Format != "steervo-documents" || manifest.Version != 1 || manifest.Documents is null || manifest.Documents.Length > 1000
                || manifest.Documents.Any(d => d is null || d.Revisions is null || d.Revisions.Length is < 1 or > 100)) throw new RuleException("InvalidImport");
            var versions = manifest.Documents.SelectMany(d => d.Revisions).ToArray();
            if (versions.Length > Evaluation.MaximumDocumentRevisions || versions.Any(v => v is null || v.Entry is null || v.Entry.Length != 38
                || !v.Entry.StartsWith("files/", StringComparison.Ordinal) || !Guid.TryParseExact(v.Entry[6..], "N", out _))
                || versions.Select(v => v.Entry).Distinct(StringComparer.Ordinal).Count() != versions.Length
                || archive.Entries.Count != versions.Length + 2 || versions.Any(v => v.Length is < 1 or > DocumentFile.MaximumBytes)
                || versions.Sum(v => v.Length) > Evaluation.MaximumDocumentBytes) throw new RuleException("InvalidImport");
            // Validate every reference and version before writing any object.
            foreach (var document in manifest.Documents)
            {
                if (!prepared.OfferIds.TryGetValue(document.OfferId, out var offerId)
                    || !document.Revisions.Select(v => v.Number).SequenceEqual(Enumerable.Range(1, document.Revisions.Length))) throw new RuleException("InvalidImport");
                prepared.Evaluation.RestoreDocument(offerId, document.Name, document.Revisions.Select(v =>
                    (v.OfferRevisionNumber, new DocumentFile(Guid.NewGuid(), v.FileName, v.Length, v.Sha256), (string?)v.Note, v.CreatedAt)));
                foreach (var version in document.Revisions)
                    if (archive.GetEntry(version.Entry)?.Length != version.Length) throw new RuleException("InvalidImport");
            }
            prepared.Evaluation.Documents.Clear();
            foreach (var document in manifest.Documents)
            {
                var files = new List<(int, DocumentFile, string?, DateTimeOffset)>();
                foreach (var version in document.Revisions)
                {
                    await using var source = archive.GetEntry(version.Entry)!.Open();
                    var file = await storage.SaveAsync(source, version.FileName, cancellationToken);
                    if (file.Length != version.Length || file.Sha256 != version.Sha256.ToLowerInvariant()) throw new RuleException("InvalidImport");
                    files.Add((version.OfferRevisionNumber, file, version.Note, version.CreatedAt));
                }
                prepared.Evaluation.RestoreDocument(prepared.OfferIds[document.OfferId], document.Name, files);
            }
            await store.CreateAsync(prepared.Evaluation, cancellationToken);
            return prepared.Evaluation.Id;
        }
        catch (Exception error) when (error is InvalidDataException or JsonException or OverflowException) { throw new RuleException("InvalidImport"); }
    }
    private static async Task<byte[]> ReadMetadataAsync(ZipArchiveEntry? entry, CancellationToken cancellationToken)
    {
        if (entry is null || entry.Length > EvaluationTransfer.MaximumBytes) throw new RuleException("InvalidImport");
        await using var input = entry.Open(); using var output = new MemoryStream();
        await DocumentStreams.CopyAsync(input, output, EvaluationTransfer.MaximumBytes, cancellationToken);
        return output.ToArray();
    }
}
