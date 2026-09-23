using Bunit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using BrewHouse.Components.Pages;
using BrewHouse.Data;
using BrewHouse.Services;
using BrewHouse.Storage;
using Wolverine;
using Xunit;

namespace BrewHouse.Tests;

public class HomeComponentTests : BunitContext
{
    private static Mock<IBidsClient> CreateBidsClientMock()
    {
        var mockBidsClient = new Mock<IBidsClient>();
        mockBidsClient.Setup(c => c.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mockBidsClient.Setup(c => c.DisposeAsync()).Returns(ValueTask.CompletedTask);
        return mockBidsClient;
    }

    [Fact]
    public async Task Click_UploadPhoto_Writes_Real_Bytes_And_Shows_Image()
    {
        var webRootPath = Directory.CreateTempSubdirectory("brewhouse-tests-wwwroot-").FullName;
        try
        {
            var sampleImageDirectory = Path.Combine(webRootPath, "images");
            Directory.CreateDirectory(sampleImageDirectory);
            var samplePhotoBytes = new byte[] { 1, 2, 3, 4 };
            await File.WriteAllBytesAsync(Path.Combine(sampleImageDirectory, "sample-lot-photo.png"), samplePhotoBytes);

            var mockBlobStore = new Mock<IBlobStore>();
            var mockEnvironment = new Mock<IWebHostEnvironment>();
            mockEnvironment.Setup(e => e.WebRootPath).Returns(webRootPath);
            var mockMessageBus = new Mock<IMessageBus>();
            var mockBidsClient = CreateBidsClientMock();
            var options = new DbContextOptionsBuilder<AuctionDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            var mockFactory = new Mock<IDbContextFactory<AuctionDbContext>>();
            mockFactory.Setup(f => f.CreateDbContext()).Returns(() => new AuctionDbContext(options));
            mockFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new AuctionDbContext(options));

            Services.AddSingleton(mockBlobStore.Object);
            Services.AddSingleton(mockEnvironment.Object);
            Services.AddSingleton(mockMessageBus.Object);
            Services.AddSingleton(mockBidsClient.Object);
            Services.AddSingleton(mockFactory.Object);

            var cut = Render<Home>();
            cut.Find("button").Click();

            Assert.Contains("Photo written to storage backend and read back successfully.", cut.Markup);
            Assert.Contains("/api/photos/auctions/coffee/Segfault-Yirgacheffe-Front.png", cut.Markup);
            mockBlobStore.Verify(
                b => b.WriteBytesAsync(
                    "auctions/coffee/Segfault-Yirgacheffe-Front.png",
                    samplePhotoBytes,
                    "image/png",
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            Directory.Delete(webRootPath, recursive: true);
        }
    }
}
