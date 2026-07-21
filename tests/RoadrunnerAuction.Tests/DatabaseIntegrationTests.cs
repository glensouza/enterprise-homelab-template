using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.EntityFrameworkCore;
using RoadrunnerAuction.Data;
using Xunit;

namespace RoadrunnerAuction.Tests;

/// <summary>
/// Integration tests against the real Aspire-orchestrated PostgreSQL (pgvector) container.
/// Requires Docker to be running. Replaces EF Core InMemory provider testing, which does
/// not enforce relational semantics or provider-specific behavior.
/// </summary>
public class DatabaseIntegrationTests
{
    [Fact]
    public async Task Postgres_Is_Reachable_And_Writable()
    {
        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.RoadrunnerAuction_AppHost>();

        await using var app = await builder.BuildAsync();
        await app.StartAsync();

        var connectionString = await app.GetConnectionStringAsync("roadrunnerdb");
        Assert.False(string.IsNullOrWhiteSpace(connectionString));

        var options = new DbContextOptionsBuilder<AuctionDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using var db = new AuctionDbContext(options);

        await db.Database.EnsureCreatedAsync();
        db.EquipmentDirectory.Add(new Equipment { Model = "CAT D9" });
        await db.SaveChangesAsync();

        Assert.True(await db.EquipmentDirectory.AnyAsync(e => e.Model == "CAT D9"));
    }
}
