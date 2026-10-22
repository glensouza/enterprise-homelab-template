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

    private static ICoffeeLotReader CreateLots()
    {
        var lots = new Mock<ICoffeeLotReader>();
        lots.Setup(reader => reader.InvalidateAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return lots.Object;
    }

    [Fact]
    public async Task Handle_ExistingCoffeeLot_Updates_CurrentBid_And_Broadcasts()
    {
        await using var db = CreateContext();
        db.CoffeeLots.Add(new CoffeeLot { Id = 1, Origin = "Ethiopia Yirgacheffe" });
        await db.SaveChangesAsync();
        var (hubContext, clientProxy) = CreateHubContext();

        await ProcessBidHandler.Handle(
            new ProcessBidMessage { CoffeeLotId = 1, BidAmount = 50000m },
            db,
            hubContext,
            CreateLots(),
            NullLogger<AuctionDbContext>.Instance);

        var coffeeLot = await db.CoffeeLots.FindAsync(1);
        Assert.Equal(50000m, coffeeLot!.CurrentBid);
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
        db.CoffeeLots.Add(new CoffeeLot { Id = 1, Origin = "Ethiopia Yirgacheffe", CurrentBid = 50000m });
        await db.SaveChangesAsync();
        var (hubContext, clientProxy) = CreateHubContext();

        await ProcessBidHandler.Handle(
            new ProcessBidMessage { CoffeeLotId = 1, BidAmount = bidAmount },
            db,
            hubContext,
            CreateLots(),
            NullLogger<AuctionDbContext>.Instance);

        var coffeeLot = await db.CoffeeLots.FindAsync(1);
        Assert.Equal(50000m, coffeeLot!.CurrentBid);
        clientProxy.Verify(
            p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_UnknownCoffeeLot_Throws()
    {
        await using var db = CreateContext();
        var (hubContext, _) = CreateHubContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ProcessBidHandler.Handle(
                new ProcessBidMessage { CoffeeLotId = 999, BidAmount = 50000m },
                db,
                hubContext,
                CreateLots(),
                NullLogger<AuctionDbContext>.Instance));
    }

    [Fact]
    public async Task Handle_LosingConcurrentBid_Does_Not_Overwrite_The_Winner()
    {
        var databaseName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<AuctionDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using (var seed = new AuctionDbContext(options))
        {
            seed.CoffeeLots.Add(new CoffeeLot { Id = 1, Origin = "Terraform Roast", CurrentBid = 100m });
            await seed.SaveChangesAsync();
        }

        await using var losing = new AuctionDbContext(options);
        var stale = await losing.CoffeeLots.FindAsync(1);
        Assert.NotNull(stale);

        await using var winning = new AuctionDbContext(options);
        var (hubContext, _) = CreateHubContext();
        await ProcessBidHandler.Handle(
            new ProcessBidMessage { CoffeeLotId = 1, BidAmount = 600m },
            winning,
            hubContext,
            CreateLots(),
            NullLogger<AuctionDbContext>.Instance);

        await ProcessBidHandler.Handle(
            new ProcessBidMessage { CoffeeLotId = 1, BidAmount = 500m },
            losing,
            hubContext,
            CreateLots(),
            NullLogger<AuctionDbContext>.Instance);

        await using var check = new AuctionDbContext(options);
        var saved = await check.CoffeeLots.FindAsync(1);
        Assert.Equal(600m, saved!.CurrentBid);
    }
}
