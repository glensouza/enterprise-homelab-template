using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using BrewHouse.Data;
using BrewHouse.Hubs;
using BrewHouse.Services;
using Xunit;

namespace BrewHouse.Tests;

public class ProcessBidHandlerTests
{
    private static AuctionDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AuctionDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AuctionDbContext(options);
    }

    private static (IHubContext<BidsHub> HubContext, Mock<IClientProxy> ClientProxy) CreateHubContext()
    {
        var clientProxy = new Mock<IClientProxy>();
        clientProxy
            .Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.All).Returns(clientProxy.Object);

        var hubContext = new Mock<IHubContext<BidsHub>>();
        hubContext.Setup(h => h.Clients).Returns(clients.Object);

        return (hubContext.Object, clientProxy);
    }

    [Fact]
    public async Task Handle_ExistingEquipment_Updates_CurrentBid_And_Broadcasts()
    {
        await using var db = CreateContext();
        db.EquipmentDirectory.Add(new Equipment { Id = 1, Model = "CAT D9" });
        await db.SaveChangesAsync();
        var (hubContext, clientProxy) = CreateHubContext();

        await ProcessBidHandler.Handle(
            new ProcessBidMessage { EquipmentId = 1, BidAmount = 50000m },
            db,
            hubContext,
            NullLogger<AuctionDbContext>.Instance);

        var equipment = await db.EquipmentDirectory.FindAsync(1);
        Assert.Equal(50000m, equipment!.CurrentBid);
        clientProxy.Verify(
            p => p.SendCoreAsync("BidPlaced", new object?[] { 1, 50000m }, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(40000)] // strictly lower than the standing bid
    [InlineData(50000)] // equal to it - a redelivery of the message that set it
    public async Task Handle_BidThatDoesNotBeatCurrent_Is_Ignored(int bidAmount)
    {
        await using var db = CreateContext();
        db.EquipmentDirectory.Add(new Equipment { Id = 1, Model = "CAT D9", CurrentBid = 50000m });
        await db.SaveChangesAsync();
        var (hubContext, clientProxy) = CreateHubContext();

        await ProcessBidHandler.Handle(
            new ProcessBidMessage { EquipmentId = 1, BidAmount = bidAmount },
            db,
            hubContext,
            NullLogger<AuctionDbContext>.Instance);

        var equipment = await db.EquipmentDirectory.FindAsync(1);
        Assert.Equal(50000m, equipment!.CurrentBid);
        clientProxy.Verify(
            p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_UnknownEquipment_Throws()
    {
        await using var db = CreateContext();
        var (hubContext, _) = CreateHubContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ProcessBidHandler.Handle(
                new ProcessBidMessage { EquipmentId = 999, BidAmount = 50000m },
                db,
                hubContext,
                NullLogger<AuctionDbContext>.Instance));
    }
}
