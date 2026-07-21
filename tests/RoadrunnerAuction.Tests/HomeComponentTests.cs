using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RoadrunnerAuction.Components.Pages;
using RoadrunnerAuction.Data;
using RoadrunnerAuction.Storage;
using Wolverine;
using Xunit;

namespace RoadrunnerAuction.Tests;

public class HomeComponentTests : BunitContext
{
    [Fact]
    public void Click_UploadPhoto_Updates_UI_Status()
    {
        var mockBlobStore = new Mock<IBlobStore>();
        var mockMessageBus = new Mock<IMessageBus>();
        var options = new DbContextOptionsBuilder<AuctionDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var mockFactory = new Mock<IDbContextFactory<AuctionDbContext>>();
        mockFactory.Setup(f => f.CreateDbContext()).Returns(() => new AuctionDbContext(options));

        Services.AddSingleton(mockBlobStore.Object);
        Services.AddSingleton(mockMessageBus.Object);
        Services.AddSingleton(mockFactory.Object);

        var cut = Render<Home>();
        cut.Find("button").Click();

        Assert.Contains("Mock photo written to storage backend successfully.", cut.Markup);
        mockBlobStore.Verify(
            b => b.WriteTextAsync("auctions/equipment/CAT-D9-Front.txt", "mock-image-data", It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
