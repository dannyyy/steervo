using System.Security.Cryptography;
using Steervo.Core;

namespace Steervo.Application.Documents;

public static class DocumentStreams
{
    public static FileStream Temporary()
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.ReadWrite, Share = FileShare.None, Options = FileOptions.Asynchronous | FileOptions.DeleteOnClose };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new(Path.Combine(Path.GetTempPath(), "steervo-" + Guid.NewGuid().ToString("N")), options);
    }
    public static async Task<(long Length, string Hash)> CopyAsync(Stream source, Stream target, long maximum, CancellationToken cancellationToken = default)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920]; long length = 0; int count;
        while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            length = checked(length + count);
            if (length > maximum) throw new RuleException("DocumentTooLarge");
            hash.AppendData(buffer, 0, count);
            await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
        return (length, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }
}
