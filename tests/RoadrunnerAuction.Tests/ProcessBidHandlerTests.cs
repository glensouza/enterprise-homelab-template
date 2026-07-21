using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RoadrunnerAuction.Data;
using RoadrunnerAuction.Services;
using Xunit;

namespace RoadrunnerAuction.Tests;

public class ProcessBidHandlerTests
{
    private static AuctionDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AuctionDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AuctionDbContext(options);
    }

    [Fact]
    public async Task Handle_ExistingEquipment_Completes()
    {
        await using var db = CreateContext();
        db.EquipmentDirectory.Add(new Equipment { Id = 1, Model = "CAT D9" });
        await db.SaveChangesAsync();

        await ProcessBidHandler.Handle(
            new ProcessBidMessage { EquipmentId = 1, BidAmount = 50000m },
            db,
            NullLogger<AuctionDbContext>.Instance);
    }

    [Fact]
    public async Task Handle_UnknownEquipment_Throws()
    {
        await using var db = CreateContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ProcessBidHandler.Handle(
                new ProcessBidMessage { EquipmentId = 999, BidAmount = 50000m },
                db,
                NullLogger<AuctionDbContext>.Instance));
    }
}
