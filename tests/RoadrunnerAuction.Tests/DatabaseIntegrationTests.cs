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

        // MigrateAsync, not EnsureCreatedAsync: EnsureCreated is a no-op when the database
        // already contains ANY table, so once Wolverine's envelope schema exists in
        // roadrunnerdb it silently skips the app's tables and every query fails with 42P01.
        // Applying the real migrations also matches how production is provisioned (ADR 11).
        await db.Database.MigrateAsync();

        // Unique per run - the AppHost keeps a persistent data volume, so a fixed literal
        // would accumulate rows across runs and break SingleAsync on the second one.
        var model = $"CAT D9 {Guid.NewGuid():N}";
        db.EquipmentDirectory.Add(new Equipment { Model = model, CurrentBid = 125000m });
        await db.SaveChangesAsync();

        var saved = await db.EquipmentDirectory.SingleAsync(e => e.Model == model);
        Assert.Equal(125000m, saved.CurrentBid);
    }
}
