using Microsoft.EntityFrameworkCore;
using BrewHouse.Data;
using BrewHouse.Services;
using Xunit;

namespace BrewHouse.Tests;

public class CoffeeLotReaderTests
{
    private sealed class MemoryCache : ILotListCache
    {
        public string? Value { get; private set; }
        public int Reads { get; private set; }
        public int Writes { get; private set; }

        public Task<string?> GetAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(Value);
        }

        public Task SetAsync(string json, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            Writes++;
            Value = json;
            return Task.CompletedTask;
        }

        public Task InvalidateAsync(CancellationToken cancellationToken = default)
        {
            Value = null;
            return Task.CompletedTask;
        }
    }

    private sealed class Factory(DbContextOptions<AuctionDbContext> options) : IDbContextFactory<AuctionDbContext>
    {
        public AuctionDbContext CreateDbContext() => new(options);

        public Task<AuctionDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }

    [Fact]
    public async Task GetLots_Loads_Once_Then_Serves_The_Cache()
    {
        var options = new DbContextOptionsBuilder<AuctionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using (var seed = new AuctionDbContext(options))
        {
            seed.CoffeeLots.Add(new CoffeeLot { Origin = "Terraform Roast", CurrentBid = 450m });
            await seed.SaveChangesAsync();
        }

        var cache = new MemoryCache();
        var reader = new CoffeeLotReader(new Factory(options), cache);

        var first = await reader.GetLotsAsync();
        var second = await reader.GetLotsAsync();

        Assert.Equal("Terraform Roast", first[0].Origin);
        Assert.Equal(first[0].CurrentBid, second[0].CurrentBid);
        Assert.Equal(1, cache.Writes);
        Assert.Equal(2, cache.Reads);
    }

    [Fact]
    public async Task Invalidate_Forces_The_Next_Read_Back_To_The_Database()
    {
        var options = new DbContextOptionsBuilder<AuctionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using (var seed = new AuctionDbContext(options))
        {
            seed.CoffeeLots.Add(new CoffeeLot { Id = 1, Origin = "Terraform Roast", CurrentBid = 450m });
            await seed.SaveChangesAsync();
        }

        var cache = new MemoryCache();
        var reader = new CoffeeLotReader(new Factory(options), cache);
        await reader.GetLotsAsync();

        await using (var update = new AuctionDbContext(options))
        {
            var lot = await update.CoffeeLots.FindAsync(1);
            lot!.CurrentBid = 950m;
            await update.SaveChangesAsync();
        }

        await reader.InvalidateAsync();
        var refreshed = await reader.GetLotsAsync();

        Assert.Equal(950m, refreshed[0].CurrentBid);
        Assert.Equal(2, cache.Writes);
    }
}
