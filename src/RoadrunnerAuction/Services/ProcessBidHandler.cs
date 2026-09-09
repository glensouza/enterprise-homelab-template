using Microsoft.AspNetCore.SignalR;
using RoadrunnerAuction.Data;
using RoadrunnerAuction.Hubs;

namespace RoadrunnerAuction.Services;

/// <summary>
/// Wolverine handler for incoming bids. Handlers are discovered automatically
/// by Wolverine at startup - no interface or registration required. Runs
/// identically regardless of which Wolverine transport carried the message
/// (ADR 07). After persisting the bid, broadcasts it to every Blazor circuit
/// on every node via the Garnet-backed SignalR backplane (ADR 08).
/// </summary>
public static class ProcessBidHandler
{
    public static async Task Handle(
        ProcessBidMessage message,
        AuctionDbContext db,
        IHubContext<BidsHub> hubContext,
        ILogger<AuctionDbContext> logger,
        CancellationToken cancellationToken = default)
    {
        var equipment = await db.EquipmentDirectory.FindAsync([message.EquipmentId], cancellationToken);
        if (equipment is null)
            throw new InvalidOperationException($"Cannot process bid: equipment {message.EquipmentId} does not exist.");

        // An auction bid must beat the standing bid. Messages can arrive late or be
        // redelivered by the durable inbox, so a stale bid is expected traffic, not an
        // error - log and drop it rather than throwing, which would send a perfectly
        // valid-but-late message around the retry/dead-letter loop forever.
        if (message.BidAmount <= equipment.CurrentBid)
        {
            logger.LogInformation(
                "Ignored bid of {BidAmount:C} for equipment {Model} (Id {EquipmentId}): does not beat the current bid of {CurrentBid:C}",
                message.BidAmount, equipment.Model, message.EquipmentId, equipment.CurrentBid);
            return;
        }

        equipment.CurrentBid = message.BidAmount;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Processed bid of {BidAmount:C} for equipment {Model} (Id {EquipmentId})",
            message.BidAmount, equipment.Model, message.EquipmentId);

        await hubContext.Clients.All.SendAsync("BidPlaced", equipment.Id, equipment.CurrentBid, cancellationToken);
    }
}
