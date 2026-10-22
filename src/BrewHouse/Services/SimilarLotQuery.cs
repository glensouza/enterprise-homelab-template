using BrewHouse.Data;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

namespace BrewHouse.Services;

public sealed class SimilarLotQuery(IDbContextFactory<AuctionDbContext> dbFactory) : ISimilarLotQuery
{
    public async Task<CoffeeLotSnapshot?> FindNearestAsync(int coffeeLotId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (db.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) != true)
            return null;

        var anchor = await db.CoffeeLots.AsNoTracking()
            .Where(lot => lot.Id == coffeeLotId)
            .Select(lot => lot.Embedding)
            .FirstOrDefaultAsync(cancellationToken);
        if (anchor is null)
            return null;

        return await db.CoffeeLots.AsNoTracking()
            .Where(lot => lot.Id != coffeeLotId && lot.Embedding != null)
            .OrderBy(lot => lot.Embedding!.CosineDistance(anchor))
            .Select(lot => new CoffeeLotSnapshot
            {
                Id = lot.Id,
                Origin = lot.Origin,
                CurrentBid = lot.CurrentBid,
            })
            .FirstOrDefaultAsync(cancellationToken);
    }
}
