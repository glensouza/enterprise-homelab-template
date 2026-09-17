using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using BrewHouse.Data;
using Xunit;

namespace BrewHouse.Tests;

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
            .CreateAsync<Projects.BrewHouse_AppHost>();

        await using var app = await builder.BuildAsync();
        await app.StartAsync();

        var connectionString = await app.GetConnectionStringAsync("brewhousedb");
        Assert.False(string.IsNullOrWhiteSpace(connectionString));

        var options = new DbContextOptionsBuilder<AuctionDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using var db = new AuctionDbContext(options);

        // MigrateAsync, not EnsureCreatedAsync: EnsureCreated is a no-op when the database
        // already contains ANY table, so once Wolverine's envelope schema exists in
        // brewhousedb it silently skips the app's tables and every query fails with 42P01.
        // Applying the real migrations also matches how production is provisioned (ADR 11).
        await db.Database.MigrateAsync();

        // Unique per run - the AppHost keeps a persistent data volume, so a fixed literal
        // would accumulate rows across runs and break SingleAsync on the second one.
        var origin = $"Ethiopia Yirgacheffe {Guid.NewGuid():N}";
        db.CoffeeLots.Add(new CoffeeLot { Origin = origin, CurrentBid = 125000m });
        await db.SaveChangesAsync();

        var saved = await db.CoffeeLots.SingleAsync(e => e.Origin == origin);
        Assert.Equal(125000m, saved.CurrentBid);
    }

    // Requested by Copilot's PR review: Postgres_Is_Reachable_And_Writable only proves an
    // empty database migrates cleanly - it would pass identically even if
    // RenameEquipmentToCoffeeLot were reverted to a destructive DropTable/CreateTable, since
    // it never inserts a row until after every migration (including this one) has already
    // run. This test instead migrates only up to the migration immediately before the rename,
    // seeds a row under the OLD schema shape (raw SQL, since the old C# model no longer
    // exists in code), then applies just the rename migration and asserts the row survived
    // under the new table/column names with its data intact.
    [Fact]
    public async Task RenameMigration_Preserves_Existing_Rows()
    {
        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.BrewHouse_AppHost>();

        await using var app = await builder.BuildAsync();
        await app.StartAsync();

        var connectionString = await app.GetConnectionStringAsync("brewhousedb");
        Assert.False(string.IsNullOrWhiteSpace(connectionString));

        var options = new DbContextOptionsBuilder<AuctionDbContext>()
            .UseNpgsql(connectionString, x => x.MigrationsAssembly(typeof(AuctionDbContext).Assembly.FullName))
            .Options;
        await using var db = new AuctionDbContext(options);

        var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();

        // Roll back to (or up to, on a fresh database) the pre-rename schema shape, seed a
        // row under the OLD table/column names, then apply just the rename on top of it.
        await migrator.MigrateAsync("20260909161435_AddEquipmentCurrentBid");

        var origin = $"Ethiopia Yirgacheffe {Guid.NewGuid():N}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"EquipmentDirectory\" (\"Model\", \"CurrentBid\") VALUES ({origin}, {125000m})");

        await migrator.MigrateAsync("20260917051217_RenameEquipmentToCoffeeLot");

        var saved = await db.CoffeeLots.SingleAsync(e => e.Origin == origin);
        Assert.Equal(125000m, saved.CurrentBid);
    }
}
