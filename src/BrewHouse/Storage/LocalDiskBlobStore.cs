namespace BrewHouse.Storage;

/// <summary>
/// Writes blobs to a local or NFS-mounted directory (e.g. the Synology NAS mount).
/// </summary>
public class LocalDiskBlobStore : IBlobStore
{
    private readonly string _rootPath;

    public LocalDiskBlobStore(string rootPath)
    {
        _rootPath = rootPath;
    }

    private string Resolve(string path)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_rootPath, path));
        if (!fullPath.StartsWith(Path.GetFullPath(_rootPath), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Path '{path}' escapes the configured storage root.", nameof(path));
        return fullPath;
    }

    public async Task WriteTextAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        var fullPath = Resolve(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, content, cancellationToken);
    }

    public Task<string> ReadTextAsync(string path, CancellationToken cancellationToken = default)
        => File.ReadAllTextAsync(Resolve(path), cancellationToken);

    public async Task WriteBytesAsync(string path, byte[] content, string contentType, CancellationToken cancellationToken = default)
    {
        // Local disk has no side-channel for object metadata the way S3 does, so
        // content type isn't persisted here - ReadBytesAsync infers it back from
        // the file extension instead (see ContentTypeFromExtension), which is fine
        // as long as callers use a real extension on the path, same assumption
        // every other local-disk static file server makes.
        var fullPath = Resolve(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllBytesAsync(fullPath, content, cancellationToken);
    }

    public async Task<(byte[] Content, string ContentType)> ReadBytesAsync(string path, CancellationToken cancellationToken = default)
    {
        var content = await File.ReadAllBytesAsync(Resolve(path), cancellationToken);
        return (content, ContentTypeFromExtension(path));
    }

    private static string ContentTypeFromExtension(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => "application/octet-stream",
    };

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Resolve(path);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        return Task.CompletedTask;
    }
}
