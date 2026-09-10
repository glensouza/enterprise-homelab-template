using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using BrewHouse.Components;
using BrewHouse.Data;
using BrewHouse.Services;
using Wolverine;
using Xunit;

namespace BrewHouse.Tests;

public class LiveBidsComponentTests : BunitContext
{
    private static Mock<IDbContextFactory<AuctionDbContext>> CreateFactoryMock(DbContextOptions<AuctionDbContext> options)
    {
        var mockFactory = new Mock<IDbContextFactory<AuctionDbContext>>();
        mockFactory.Setup(f => f.CreateDbContext()).Returns(() => new AuctionDbContext(options));
        mockFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AuctionDbContext(options));
        return mockFactory;
    }

    private static Mock<IBidsClient> CreateBidsClientMock()
    {
        var mockBidsClient = new Mock<IBidsClient>();
        mockBidsClient.Setup(c => c.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mockBidsClient.Setup(c => c.DisposeAsync()).Returns(ValueTask.CompletedTask);
        return mockBidsClient;
    }

    [Fact]
    public void PlaceBid_Click_Publishes_ProcessBidMessage()
    {
        var options = new DbContextOptionsBuilder<AuctionDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        using (var seed = new AuctionDbContext(options))
        {
            seed.EquipmentDirectory.Add(new Equipment { Id = 1, Model = "CAT D9", CurrentBid = 1000m });
            seed.SaveChanges();
        }

        var mockMessageBus = new Mock<IMessageBus>();
        Services.AddSingleton(mockMessageBus.Object);
        Services.AddSingleton(CreateFactoryMock(options).Object);
        Services.AddSingleton(CreateBidsClientMock().Object);

        var cut = Render<LiveBids>();
        cut.WaitForState(() => cut.Markup.Contains("CAT D9"));

        cut.Find("button").Click();

        mockMessageBus.Verify(
            b => b.PublishAsync(
                It.Is<ProcessBidMessage>(m => m.EquipmentId == 1 && m.BidAmount == 1500m),
                It.IsAny<DeliveryOptions>()),
            Times.Once);
    }

    [Fact]
    public void BidPlaced_Broadcast_Updates_Rendered_CurrentBid_Without_Publishing()
    {
        var options = new DbContextOptionsBuilder<AuctionDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        using (var seed = new AuctionDbContext(options))
        {
            seed.EquipmentDirectory.Add(new Equipment { Id = 1, Model = "CAT D9", CurrentBid = 1000m });
            seed.SaveChanges();
        }

        var mockMessageBus = new Mock<IMessageBus>();
        var mockBidsClient = CreateBidsClientMock();
        Services.AddSingleton(mockMessageBus.Object);
        Services.AddSingleton(CreateFactoryMock(options).Object);
        Services.AddSingleton(mockBidsClient.Object);

        var cut = Render<LiveBids>();
        cut.WaitForState(() => cut.Markup.Contains("CAT D9"));

        // Simulate a bid broadcast arriving from another node via the Garnet backplane.
        mockBidsClient.Raise(c => c.BidPlaced += null, 1, 9999m);

        cut.WaitForState(() => cut.Find("[data-testid='bid-1']").TextContent.Contains("9,999"));
        mockMessageBus.Verify(b => b.PublishAsync(It.IsAny<ProcessBidMessage>(), It.IsAny<DeliveryOptions>()), Times.Never);
    }
}
