using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pgvector.EntityFrameworkCore;

namespace BrewHouse.Data;

/// <summary>
/// Used by EF Core tooling (dotnet ef migrations add/bundle) at design time.
/// The placeholder connection string never gets connected - tooling only needs the model.
/// </summary>
public class AuctionDbContextFactory : IDesignTimeDbContextFactory<AuctionDbContext>
{
    public AuctionDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AuctionDbContext>()
            .UseNpgsql("Host=localhost;Database=brewhouse_design_time", options => options.UseVector())
            .Options;
        return new AuctionDbContext(options);
    }
}
