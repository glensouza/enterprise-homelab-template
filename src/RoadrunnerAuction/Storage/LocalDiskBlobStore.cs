namespace RoadrunnerAuction.Storage;

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

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Resolve(path);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        return Task.CompletedTask;
    }
}
