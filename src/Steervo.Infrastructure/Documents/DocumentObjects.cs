using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace Steervo.Infrastructure.Documents;

/// <summary>
/// Configuration of the place document contents are kept: a local directory or an S3 bucket.
/// </summary>
public sealed class DocumentStorageOptions
{
    public string Provider { get; set; } = "FileSystem";

    public string Path { get; set; } = ".data/documents";

    public string Endpoint { get; set; } = "";

    public string Region { get; set; } = "garage";

    public string Bucket { get; set; } = "";

    public string AccessKey { get; set; } = "";

    public string SecretKey { get; set; } = "";
}

/// <summary>
/// Raw object store for document contents, addressed by content id.
/// </summary>
public interface IDocumentObjects
{
    Task PutAsync(Guid id, Stream input, CancellationToken cancellationToken);

    Task CopyToAsync(Guid id, Stream output, CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// Keeps each document as one file in a single directory.
/// </summary>
public sealed class FileSystemDocumentObjects(IOptions<DocumentStorageOptions> options)
    : IDocumentObjects
{
    private const int ReadBufferBytes = 81920;

    private readonly string directory = System.IO.Path.GetFullPath(options.Value.Path);

    public async Task PutAsync(Guid id, Stream input, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);

        // CreateNew refuses to overwrite: a content id is written exactly once.
        var fileOptions = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Options = FileOptions.Asynchronous,
        };

        if (!OperatingSystem.IsWindows())
        {
            fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        await using var file = new FileStream(FilePath(id), fileOptions);
        await input.CopyToAsync(file, cancellationToken);
        await file.FlushAsync(cancellationToken);

        // The upload counts as stored only once it has reached the disk.
        file.Flush(flushToDisk: true);
    }

    public async Task CopyToAsync(Guid id, Stream output, CancellationToken cancellationToken)
    {
        await using var file = new FileStream(
            FilePath(id),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            ReadBufferBytes,
            FileOptions.Asynchronous
        );

        await file.CopyToAsync(output, cancellationToken);
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        File.Delete(FilePath(id));

        return Task.CompletedTask;
    }

    private string FilePath(Guid id) => System.IO.Path.Combine(directory, id.ToString("N"));
}

/// <summary>
/// Keeps each document as one object in an S3 compatible bucket.
/// </summary>
public sealed class S3DocumentObjects : IDocumentObjects, IDisposable
{
    private readonly AmazonS3Client client;
    private readonly string bucket;

    public S3DocumentObjects(IOptions<DocumentStorageOptions> options)
    {
        var settings = options.Value;

        if (!IsPlainEndpoint(settings.Endpoint) || !HasBucketAndCredentials(settings))
        {
            throw new InvalidOperationException(
                "DocumentStorage requires an S3 endpoint, region, bucket and credentials."
            );
        }

        bucket = settings.Bucket;
        client = new AmazonS3Client(
            new BasicAWSCredentials(settings.AccessKey, settings.SecretKey),
            new AmazonS3Config
            {
                ServiceURL = settings.Endpoint,
                AuthenticationRegion = settings.Region,
                ForcePathStyle = true,
                Timeout = TimeSpan.FromSeconds(60),
                MaxErrorRetry = 1,
                LogResponse = false,
                DisableLogging = true,
            }
        );
    }

    public async Task PutAsync(Guid id, Stream input, CancellationToken cancellationToken)
    {
        var request = new PutObjectRequest
        {
            BucketName = bucket,
            Key = Key(id),
            InputStream = input,
            ContentType = "application/octet-stream",
            AutoCloseStream = false,
            UseChunkEncoding = false,
        };

        await client.PutObjectAsync(request, cancellationToken);
    }

    public async Task CopyToAsync(Guid id, Stream output, CancellationToken cancellationToken)
    {
        using var response = await client.GetObjectAsync(bucket, Key(id), cancellationToken);

        await response.ResponseStream.CopyToAsync(output, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await client.DeleteObjectAsync(bucket, Key(id), cancellationToken);
    }

    public void Dispose() => client.Dispose();

    // The endpoint must be the bare service address: no credentials, path, query or fragment.
    private static bool IsPlainEndpoint(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var endpoint))
        {
            return false;
        }

        return endpoint.Scheme is "http" or "https"
            && endpoint.UserInfo.Length == 0
            && endpoint.Query.Length == 0
            && endpoint.AbsolutePath == "/"
            && endpoint.Fragment.Length == 0;
    }

    private static bool HasBucketAndCredentials(DocumentStorageOptions settings)
    {
        return !string.IsNullOrWhiteSpace(settings.Bucket)
            && !string.IsNullOrWhiteSpace(settings.Region)
            && !string.IsNullOrWhiteSpace(settings.AccessKey)
            && !string.IsNullOrWhiteSpace(settings.SecretKey);
    }

    private static string Key(Guid id) => "documents/" + id.ToString("N");
}
