using System.Security.Cryptography;
using Steervo.Core;

namespace Steervo.Application.Documents;

/// <summary>Stream helpers for handling uploaded and exported files of limited size.</summary>
public static class DocumentStreams
{
    private const int CopyBufferBytes = 81920;

    /// <summary>
    /// Creates a private temporary file that deletes itself when the stream is closed.
    /// </summary>
    public static FileStream Temporary()
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.DeleteOnClose,
        };

        // Only the owner may read the file; Windows has no such mode.
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        var fileName = "steervo-" + Guid.NewGuid().ToString("N");

        return new(Path.Combine(Path.GetTempPath(), fileName), options);
    }

    /// <summary>
    /// Copies the source to the target and returns the number of bytes and their SHA-256 hash in
    /// lower-case hexadecimal. Fails as soon as more than the allowed number of bytes was read.
    /// </summary>
    public static async Task<(long Length, string Hash)> CopyAsync(
        Stream source,
        Stream target,
        long maximum,
        CancellationToken cancellationToken = default
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[CopyBufferBytes];
        long length = 0;

        while (true)
        {
            var count = await source.ReadAsync(buffer, cancellationToken);

            if (count <= 0)
            {
                break;
            }

            length = checked(length + count);

            if (length > maximum)
            {
                throw new RuleException("DocumentTooLarge");
            }

            hash.AppendData(buffer, 0, count);
            await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }

        return (length, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }
}
