using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RoadrunnerAuction.Data;

/// <summary>
/// Used by EF Core tooling (dotnet ef migrations add/bundle) at design time.
/// The placeholder connection string never gets connected - tooling only needs the model.
/// </summary>
public class AuctionDbContextFactory : IDesignTimeDbContextFactory<AuctionDbContext>
{
    public AuctionDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AuctionDbContext>()
            .UseNpgsql("Host=localhost;Database=roadrunner_design_time")
            .Options;
        return new AuctionDbContext(options);
    }
}
