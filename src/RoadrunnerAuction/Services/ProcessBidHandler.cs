using RoadrunnerAuction.Data;

namespace RoadrunnerAuction.Services;

/// <summary>
/// Wolverine handler for incoming bids. Handlers are discovered automatically
/// by Wolverine at startup - no interface or registration required.
/// </summary>
public static class ProcessBidHandler
{
    public static async Task Handle(ProcessBidMessage message, AuctionDbContext db, ILogger<AuctionDbContext> logger)
    {
        var equipment = await db.EquipmentDirectory.FindAsync(message.EquipmentId);
        if (equipment is null)
            throw new InvalidOperationException($"Cannot process bid: equipment {message.EquipmentId} does not exist.");

        logger.LogInformation("Processed bid of {BidAmount:C} for equipment {Model} (Id {EquipmentId})",
            message.BidAmount, equipment.Model, message.EquipmentId);
    }
}
