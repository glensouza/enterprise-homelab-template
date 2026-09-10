using Microsoft.EntityFrameworkCore;
namespace BrewHouse.Data;
public class AuctionDbContext : DbContext {
    public AuctionDbContext(DbContextOptions<AuctionDbContext> options) : base(options) { }
    public DbSet<Equipment> EquipmentDirectory { get; set; }
}
public class Equipment {
    public int Id { get; set; }
    public string Model { get; set; } = string.Empty;
    public decimal CurrentBid { get; set; }
}
public record ProcessBidMessage {
    public int EquipmentId { get; init; }
    public decimal BidAmount { get; init; }
}
