using StackExchange.Redis;

namespace BrewHouse.Services;

/// <summary>
/// The application cache ADR 04 named and the app previously did not have. SignalR pub/sub and
/// the data-protection key ring stay on their own connections; this key is the one a MONITOR
/// session should see on a page load. ADR 130.
/// </summary>
public sealed class GarnetLotListCache(IConnectionMultiplexer redis) : ILotListCache
{
    public const string Key = "brewhouse:coffee-lots";
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(5);

    public async Task<string?> GetAsync(CancellationToken cancellationToken = default)
    {
        var value = await redis.GetDatabase().StringGetAsync(Key);
        return value.IsNullOrEmpty ? null : value.ToString();
    }

    public async Task SetAsync(string json, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        await redis.GetDatabase().StringSetAsync(Key, json, ttl);
    }

    public async Task InvalidateAsync(CancellationToken cancellationToken = default)
    {
        await redis.GetDatabase().KeyDeleteAsync(Key);
    }
}
