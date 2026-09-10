using Bunit;
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
    public void Click_UploadPhoto_Updates_UI_Status()
    {
        var mockBlobStore = new Mock<IBlobStore>();
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
        Services.AddSingleton(mockMessageBus.Object);
        Services.AddSingleton(mockBidsClient.Object);
        Services.AddSingleton(mockFactory.Object);

        var cut = Render<Home>();
        cut.Find("button").Click();

        Assert.Contains("Mock photo written to storage backend successfully.", cut.Markup);
        mockBlobStore.Verify(
            b => b.WriteTextAsync("auctions/equipment/CAT-D9-Front.txt", "mock-image-data", It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
