using BrewHouse.Data;

namespace BrewHouse.Services;

public interface ICoffeeLotReader
{
    Task<IReadOnlyList<CoffeeLotSnapshot>> GetLotsAsync(CancellationToken cancellationToken = default);

    Task InvalidateAsync(CancellationToken cancellationToken = default);
}

public interface ILotListCache
{
    Task<string?> GetAsync(CancellationToken cancellationToken = default);

    Task SetAsync(string json, TimeSpan ttl, CancellationToken cancellationToken = default);

    Task InvalidateAsync(CancellationToken cancellationToken = default);
}

public interface ISimilarLotQuery
{
    Task<CoffeeLotSnapshot?> FindNearestAsync(int coffeeLotId, CancellationToken cancellationToken = default);
}
