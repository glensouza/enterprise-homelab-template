using Amazon.S3;
using Amazon.S3.Model;

namespace BrewHouse.Storage;

/// <summary>
/// Writes blobs to an S3-compatible bucket. Works against real Amazon S3 (leave
/// ServiceURL unset, use the default AWS credential chain / a region) or any
/// S3-compatible endpoint - e.g. the Floci emulator in the PR preview stack -
/// by setting BlobStorage:S3:ServiceUrl and ForcePathStyle.
/// </summary>
public class S3BlobStore(IAmazonS3 client, string bucketName) : IBlobStore
{
    private readonly IAmazonS3 _client = client;
    private readonly string _bucketName = bucketName;

    public Task WriteTextAsync(string path, string content, CancellationToken cancellationToken = default)
        => _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = path,
            ContentBody = content,
        }, cancellationToken);

    public async Task<string> ReadTextAsync(string path, CancellationToken cancellationToken = default)
    {
        using var response = await _client.GetObjectAsync(_bucketName, path, cancellationToken);
        using var reader = new StreamReader(response.ResponseStream);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
        => _client.DeleteObjectAsync(_bucketName, path, cancellationToken);
}
