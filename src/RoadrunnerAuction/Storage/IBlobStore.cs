namespace RoadrunnerAuction.Storage;

/// <summary>
/// Cloud-agnostic blob storage abstraction owned by the application.
/// Implementations: LocalDiskBlobStore (default). Add Azure Blob / S3 / GCS
/// adapters later by implementing this interface and swapping the DI registration.
/// </summary>
public interface IBlobStore
{
    Task WriteTextAsync(string path, string content, CancellationToken cancellationToken = default);
    Task<string> ReadTextAsync(string path, CancellationToken cancellationToken = default);
    Task DeleteAsync(string path, CancellationToken cancellationToken = default);
}
