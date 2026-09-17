using Microsoft.EntityFrameworkCore;
namespace BrewHouse.Data;
public class AuctionDbContext : DbContext {
    public AuctionDbContext(DbContextOptions<AuctionDbContext> options) : base(options) { }
    public DbSet<CoffeeLot> CoffeeLots { get; set; }
}
public class CoffeeLot {
    public int Id { get; set; }
    public string Origin { get; set; } = string.Empty;
    public decimal CurrentBid { get; set; }
}
public record ProcessBidMessage {
    public int CoffeeLotId { get; init; }
    public decimal BidAmount { get; init; }
}
