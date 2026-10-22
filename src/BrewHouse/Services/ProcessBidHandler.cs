using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using BrewHouse.Data;
using BrewHouse.Hubs;

namespace BrewHouse.Services;

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
        ICoffeeLotReader lots,
        ILogger<AuctionDbContext> logger,
        CancellationToken cancellationToken = default)
    {
        // Two attempts: the second covers the loser of a race. A further conflict is a real
        // failure and goes through Wolverine's retry / dead-letter path.
        const int maxAttempts = 2;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var coffeeLot = await db.CoffeeLots.FindAsync([message.CoffeeLotId], cancellationToken);
            if (coffeeLot is null)
                throw new InvalidOperationException($"Cannot process bid: coffee lot {message.CoffeeLotId} does not exist.");

            // An auction bid must beat the standing bid. Messages can arrive late or be
            // redelivered by the durable inbox, so a stale bid is expected traffic, not an
            // error - log and drop it rather than throwing, which would send a perfectly
            // valid-but-late message around the retry/dead-letter loop forever.
            if (!BidRules.BeatsStandingBid(coffeeLot.CurrentBid, message.BidAmount))
            {
                logger.LogInformation(
                    "Ignored bid of {BidAmount:C} for coffee lot {Origin} (Id {CoffeeLotId}): does not beat the current bid of {CurrentBid:C}",
                    message.BidAmount, coffeeLot.Origin, message.CoffeeLotId, coffeeLot.CurrentBid);
                return;
            }

            coffeeLot.CurrentBid = message.BidAmount;
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException) when (attempt < maxAttempts)
            {
                await db.Entry(coffeeLot).ReloadAsync(cancellationToken);
                continue;
            }

            logger.LogInformation("Processed bid of {BidAmount:C} for coffee lot {Origin} (Id {CoffeeLotId})",
                message.BidAmount, coffeeLot.Origin, message.CoffeeLotId);

            await lots.InvalidateAsync(cancellationToken);
            await hubContext.Clients.All.SendAsync("BidPlaced", coffeeLot.Id, coffeeLot.CurrentBid, cancellationToken);
            return;
        }
    }
}
