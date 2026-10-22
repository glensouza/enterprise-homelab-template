using System.Text.Json;
using BrewHouse.Data;
using Microsoft.EntityFrameworkCore;

namespace BrewHouse.Services;

public sealed class CoffeeLotReader(IDbContextFactory<AuctionDbContext> dbFactory, ILotListCache cache) : ICoffeeLotReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<CoffeeLotSnapshot>> GetLotsAsync(CancellationToken cancellationToken = default)
    {
        var cached = await cache.GetAsync(cancellationToken);
        if (cached is not null)
        {
            var hit = JsonSerializer.Deserialize<List<CoffeeLotSnapshot>>(cached, JsonOptions);
            if (hit is not null)
                return hit;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var lots = await db.CoffeeLots.AsNoTracking()
            .OrderBy(lot => lot.Origin)
            .Select(lot => new CoffeeLotSnapshot
            {
                Id = lot.Id,
                Origin = lot.Origin,
                CurrentBid = lot.CurrentBid,
            })
            .ToListAsync(cancellationToken);

        await cache.SetAsync(JsonSerializer.Serialize(lots, JsonOptions), GarnetLotListCache.DefaultTtl, cancellationToken);
        return lots;
    }

    public Task InvalidateAsync(CancellationToken cancellationToken = default)
        => cache.InvalidateAsync(cancellationToken);
}
