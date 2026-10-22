using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace BrewHouse.Data;

public class AuctionDbContext : DbContext
{
    public AuctionDbContext(DbContextOptions<AuctionDbContext> options) : base(options) { }

    public DbSet<CoffeeLot> CoffeeLots { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Unit tests use the InMemory provider, which cannot map Pgvector.Vector. The column
        // exists only on Npgsql; nearest-lot queries already refuse every other provider.
        var mapVector = Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory";
        if (mapVector)
            modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<CoffeeLot>(entity =>
        {
            // The standing bid is the concurrency token. Two handlers that both read the same
            // price cannot both write; the loser reloads and drops the bid if it no longer wins.
            entity.Property(lot => lot.CurrentBid).IsConcurrencyToken();
            if (mapVector)
                entity.Property(lot => lot.Embedding).HasColumnType($"vector({CoffeeLotEmbedding.Dimensions})");
            else
                entity.Ignore(lot => lot.Embedding);
        });
    }
}

public class CoffeeLot
{
    public int Id { get; set; }
    public string Origin { get; set; } = string.Empty;
    public decimal CurrentBid { get; set; }
    public Vector? Embedding { get; set; }
}
public record ProcessBidMessage {
    public int CoffeeLotId { get; init; }
    public decimal BidAmount { get; init; }
}
