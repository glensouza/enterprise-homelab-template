namespace BrewHouse.Services;

/// <summary>
/// The one bid rule the button and the HTTP API both have to honour. The handler still
/// enforces it under a concurrency token, because the value this saw can already be stale.
/// </summary>
public static class BidRules
{
    public static bool BeatsStandingBid(decimal standingBid, decimal bidAmount) => bidAmount > standingBid;
}
