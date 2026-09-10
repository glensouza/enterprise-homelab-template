using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using BrewHouse.Storage;
using Testcontainers.Floci;
using Xunit;

namespace BrewHouse.Tests;

/// <summary>
/// Integration tests against the real S3 API surface, provided by the Floci
/// emulator (Testcontainers.Floci). Requires Docker to be running. Verifies
/// S3BlobStore against provider-real behavior rather than a mocked IAmazonS3.
/// </summary>
public class S3BlobStoreIntegrationTests : IAsyncLifetime
{
    private const string BucketName = "brewhouse-auction-blobs-test";
    private readonly FlociContainer _floci = new FlociBuilder("floci/floci:1.5.13").Build();

    public Task InitializeAsync() => _floci.StartAsync();

    public Task DisposeAsync() => _floci.DisposeAsync().AsTask();

    private AmazonS3Client CreateClient() => new(
        new BasicAWSCredentials(FlociBuilder.AccessKey, FlociBuilder.SecretKey),
        new AmazonS3Config { ServiceURL = _floci.GetConnectionString(), ForcePathStyle = true });

    [Fact]
    public async Task WriteReadDelete_RoundTrips_Through_S3_Compatible_Storage()
    {
        using var client = CreateClient();
        await client.PutBucketAsync(BucketName);
        var store = new S3BlobStore(client, BucketName);

        await store.WriteTextAsync("auctions/equipment/CAT-D9-Front.txt", "mock-image-data");
        var content = await store.ReadTextAsync("auctions/equipment/CAT-D9-Front.txt");
        Assert.Equal("mock-image-data", content);

        await store.DeleteAsync("auctions/equipment/CAT-D9-Front.txt");
        await Assert.ThrowsAsync<NoSuchKeyException>(() => store.ReadTextAsync("auctions/equipment/CAT-D9-Front.txt"));
    }
}
