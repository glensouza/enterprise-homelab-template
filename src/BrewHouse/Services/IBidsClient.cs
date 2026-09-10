namespace BrewHouse.Services;

/// <summary>
/// Client-side subscription to live bid updates broadcast by ProcessBidHandler.
/// Abstracted from the concrete SignalR connection so LiveBids.razor is testable
/// with bUnit (no real Kestrel/hub endpoint needed in-process).
/// </summary>
public interface IBidsClient : IAsyncDisposable
{
    event Action<int, decimal>? BidPlaced;

    Task StartAsync(CancellationToken cancellationToken = default);
}
