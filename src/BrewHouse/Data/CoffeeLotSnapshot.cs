namespace BrewHouse.Data;

public record CoffeeLotSnapshot
{
    public int Id { get; set; }
    public string Origin { get; set; } = "";
    public decimal CurrentBid { get; set; }
}

/// <summary>
/// v2 list item. The only break from <see cref="CoffeeLotSnapshot"/> is the renamed money field,
/// so a client pinned to v1 keeps compiling. ADR 134.
/// </summary>
public record AuctionV2
{
    public int Id { get; set; }
    public string Origin { get; set; } = "";
    public decimal Bid { get; set; }
}

public record PlaceBidRequest
{
    public decimal Amount { get; set; }
}
