using System.Security.Cryptography;
using Amazon.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Steervo.Application.Documents;
using Steervo.Core;
using Steervo.Infrastructure.Data;

namespace Steervo.Infrastructure.Documents;

/// <summary>
/// A stored object that will be deleted once <see cref="AvailableAt"/> has passed, unless a
/// document revision refers to it by then.
/// </summary>
public sealed class PendingDocumentDeletion
{
    public Guid Id { get; set; }

    public DateTimeOffset AvailableAt { get; set; }
}

/// <summary>
/// Stores uploaded document contents and reads them back, verifying length and hash.
/// </summary>
public sealed class DocumentStorage(
    AppDbContext db,
    IDocumentObjects objects,
    ILogger<DocumentStorage> logger
) : IDocumentStorage
{
    private const string Unavailable = "DocumentStorageUnavailable";

    // An upload that is not attached to a document within this time is removed again.
    private const int UnattachedUploadHours = 1;

    public async Task<DocumentFile> SaveAsync(
        Stream input,
        string fileName,
        CancellationToken cancellationToken = default
    )
    {
        await using var buffer = DocumentStreams.Temporary();
        var result = await DocumentStreams.CopyAsync(
            input,
            buffer,
            DocumentFile.MaximumBytes,
            cancellationToken
        );
        var file = new DocumentFile(
            Guid.NewGuid(),
            fileName,
            result.Length,
            result.Hash
        ).Validated();

        // A durable cleanup intent precedes the object write, including interrupted uploads.
        var cleanupIntent = new PendingDocumentDeletion
        {
            Id = file.ContentId,
            AvailableAt = DateTimeOffset.UtcNow.AddHours(UnattachedUploadHours),
        };
        db.Set<PendingDocumentDeletion>().Add(cleanupIntent);
        await db.SaveChangesAsync(cancellationToken);

        buffer.Position = 0;

        try
        {
            await objects.PutAsync(file.ContentId, buffer, cancellationToken);
        }
        catch (Exception error) when (DocumentFailures.IsStorageFailure(error))
        {
            logger.LogWarning(
                "Document upload failed ({ErrorType}).",
                DocumentFailures.ErrorType(error)
            );

            throw new RuleException(Unavailable);
        }

        return file;
    }

    public async Task<Stream> OpenAsync(
        DocumentFile file,
        CancellationToken cancellationToken = default
    )
    {
        var buffer = DocumentStreams.Temporary();

        try
        {
            await objects.CopyToAsync(
                file.ContentId,
                new LimitedWriteStream(buffer, file.Length),
                cancellationToken
            );

            buffer.Position = 0;
            var digest = await SHA256.HashDataAsync(buffer, cancellationToken);

            // Content that does not match the recorded length and hash is never handed out.
            if (buffer.Length != file.Length || Convert.ToHexStringLower(digest) != file.Sha256)
            {
                throw new RuleException(Unavailable);
            }

            buffer.Position = 0;

            return buffer;
        }
        catch (Exception error)
        {
            // The caller only owns the buffer when it is returned.
            await buffer.DisposeAsync();

            if (!DocumentFailures.IsStorageFailure(error))
            {
                throw;
            }

            logger.LogWarning(
                "Document download failed ({ErrorType}).",
                DocumentFailures.ErrorType(error)
            );

            throw new RuleException(Unavailable);
        }
    }

    // Stops a download as soon as the store delivers more bytes than the document is known to have.
    private sealed class LimitedWriteStream(Stream target, long limit) : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => target.Length;

        public override long Position
        {
            get => target.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush() => target.Flush();

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (target.Position + count > limit)
            {
                throw new RuleException(Unavailable);
            }

            target.Write(buffer, offset, count);
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            if (target.Position + buffer.Length > limit)
            {
                throw new RuleException(Unavailable);
            }

            await target.WriteAsync(buffer, cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}

/// <summary>
/// Deletes stored objects whose pending deletion is due and that no document revision uses.
/// </summary>
public sealed class DocumentCleanup(
    AppDbContext db,
    IDocumentObjects objects,
    ILogger<DocumentCleanup> logger
)
{
    // A bounded run happens inside a user request and must not hold it up.
    private static readonly TimeSpan BoundedRunTime = TimeSpan.FromSeconds(3);

    // An unbounded run drains the whole backlog and gets correspondingly more time.
    private static readonly TimeSpan UnboundedRunTime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Processes up to <paramref name="maximum"/> due deletions and returns how many were completed.
    /// Storage and database failures are logged, not thrown: the remaining entries are retried later.
    /// </summary>
    public async Task<int> RunAsync(int maximum = 20, CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(maximum == int.MaxValue ? UnboundedRunTime : BoundedRunTime);
        var deadlineToken = deadline.Token;

        var count = 0;

        try
        {
            while (count < maximum)
            {
                var deleted = await DeleteNextAsync(deadlineToken);

                if (!deleted)
                {
                    break;
                }

                count++;
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            logger.LogInformation("Document cleanup paused; remaining deletions will be retried.");
        }
        catch (Exception error)
            when (DocumentFailures.IsStorageFailure(error) || error is NpgsqlException)
        {
            logger.LogWarning(
                "Document cleanup will be retried; pending deletions remain in the database ({ErrorType}).",
                DocumentFailures.ErrorType(error)
            );
        }

        return count;
    }

    // Handles the oldest due deletion in a transaction of its own. Returns false when nothing is due.
    private async Task<bool> DeleteNextAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // SKIP LOCKED lets concurrent cleanup runs work on different entries.
        var pending = await db.Set<PendingDocumentDeletion>()
            .FromSqlRaw(
                "SELECT * FROM \"PendingDocumentDeletion\" WHERE \"AvailableAt\" <= CURRENT_TIMESTAMP ORDER BY \"AvailableAt\" LIMIT 1 FOR UPDATE SKIP LOCKED"
            )
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            return false;
        }

        var id = pending[0].Id;
        var isReferenced = await db.Set<DocumentRevision>()
            .AnyAsync(revision => revision.File.ContentId == id, cancellationToken);

        if (!isReferenced)
        {
            await objects.DeleteAsync(id, cancellationToken);
        }

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM \"PendingDocumentDeletion\" WHERE \"Id\" = {id}",
            cancellationToken
        );
        await transaction.CommitAsync(cancellationToken);

        return true;
    }
}

// Shared classification of the failures a document store can run into.
internal static class DocumentFailures
{
    public static bool IsStorageFailure(Exception error)
    {
        return error
            is IOException
                or UnauthorizedAccessException
                or AmazonServiceException
                or HttpRequestException;
    }

    // Logs name the kind of failure only; messages may carry paths, hosts or keys.
    public static string ErrorType(Exception error)
    {
        return error is AmazonServiceException aws ? aws.ErrorCode : error.GetType().Name;
    }
}
