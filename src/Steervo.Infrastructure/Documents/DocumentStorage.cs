using Amazon.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Steervo.Application.Documents;
using Steervo.Core;
using Steervo.Infrastructure.Data;

namespace Steervo.Infrastructure.Documents;

public sealed class PendingDocumentDeletion
{
    public Guid Id { get; set; }
    public DateTimeOffset AvailableAt { get; set; }
}

public sealed class DocumentStorage(AppDbContext db, IDocumentObjects objects, ILogger<DocumentStorage> logger) : IDocumentStorage
{
    public async Task<DocumentFile> SaveAsync(Stream input, string fileName, CancellationToken cancellationToken = default)
    {
        await using var buffer = DocumentStreams.Temporary();
        var result = await DocumentStreams.CopyAsync(input, buffer, DocumentFile.MaximumBytes, cancellationToken);
        var file = new DocumentFile(Guid.NewGuid(), fileName, result.Length, result.Hash).Validated();
        // A durable cleanup intent precedes the object write, including interrupted uploads.
        db.Set<PendingDocumentDeletion>().Add(new() { Id = file.ContentId, AvailableAt = DateTimeOffset.UtcNow.AddHours(1) });
        await db.SaveChangesAsync(cancellationToken);
        buffer.Position = 0;
        try { await objects.PutAsync(file.ContentId, buffer, cancellationToken); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or AmazonServiceException or HttpRequestException)
        {
            logger.LogWarning("Document upload failed ({ErrorType}).", error is AmazonServiceException aws ? aws.ErrorCode : error.GetType().Name);
            throw new RuleException("DocumentStorageUnavailable");
        }
        return file;
    }
    public async Task<Stream> OpenAsync(DocumentFile file, CancellationToken cancellationToken = default)
    {
        var buffer = DocumentStreams.Temporary();
        try
        {
            await objects.CopyToAsync(file.ContentId, new LimitedWriteStream(buffer, file.Length), cancellationToken);
            buffer.Position = 0;
            var digest = await System.Security.Cryptography.SHA256.HashDataAsync(buffer, cancellationToken);
            if (buffer.Length != file.Length || Convert.ToHexStringLower(digest) != file.Sha256) throw new RuleException("DocumentStorageUnavailable");
            buffer.Position = 0; return buffer;
        }
        catch (Exception error)
        {
            await buffer.DisposeAsync();
            if (error is not (IOException or UnauthorizedAccessException or AmazonServiceException or HttpRequestException)) throw;
            logger.LogWarning("Document download failed ({ErrorType}).", error is AmazonServiceException aws ? aws.ErrorCode : error.GetType().Name);
            throw new RuleException("DocumentStorageUnavailable");
        }
    }
    private sealed class LimitedWriteStream(Stream target, long limit) : Stream
    {
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => target.Length; public override long Position { get => target.Position; set => throw new NotSupportedException(); }
        public override void Flush() => target.Flush();
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (target.Position + count > limit) throw new RuleException("DocumentStorageUnavailable");
            target.Write(buffer, offset, count);
        }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (target.Position + buffer.Length > limit) throw new RuleException("DocumentStorageUnavailable");
            await target.WriteAsync(buffer, cancellationToken);
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}

public sealed class DocumentCleanup(AppDbContext db, IDocumentObjects objects, ILogger<DocumentCleanup> logger)
{
    public async Task<int> RunAsync(int maximum = 20, CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(maximum == int.MaxValue ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(3));
        cancellationToken = deadline.Token;
        var count = 0;
        try
        {
            while (count < maximum)
            {
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                var pending = await db.Set<PendingDocumentDeletion>().FromSqlRaw("SELECT * FROM \"PendingDocumentDeletion\" WHERE \"AvailableAt\" <= CURRENT_TIMESTAMP ORDER BY \"AvailableAt\" LIMIT 1 FOR UPDATE SKIP LOCKED").AsNoTracking().ToListAsync(cancellationToken);
                if (pending.Count == 0) break;
                var id = pending[0].Id;
                if (!await db.Set<DocumentRevision>().AnyAsync(r => r.File.ContentId == id, cancellationToken))
                    await objects.DeleteAsync(id, cancellationToken);
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"PendingDocumentDeletion\" WHERE \"Id\" = {id}", cancellationToken);
                await transaction.CommitAsync(cancellationToken); count++;
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            logger.LogInformation("Document cleanup paused; remaining deletions will be retried.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or AmazonServiceException or HttpRequestException or Npgsql.NpgsqlException)
        {
            logger.LogWarning("Document cleanup will be retried; pending deletions remain in the database ({ErrorType}).", error is AmazonServiceException aws ? aws.ErrorCode : error.GetType().Name);
        }
        return count;
    }
}
