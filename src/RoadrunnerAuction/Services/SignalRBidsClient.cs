using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace RoadrunnerAuction.Services;

/// <inheritdoc cref="IBidsClient" />
public class SignalRBidsClient : IBidsClient
{
    private readonly HubConnection _connection;

    public event Action<int, decimal>? BidPlaced;

    // Explicit constructor rather than a primary one: the hub URI needs branching
    // logic and the "BidPlaced" subscription has to run after the connection exists.
    public SignalRBidsClient(NavigationManager navigationManager, IConfiguration configuration)
    {
        // Realtime:HubBaseUrl points the circuit at its OWN node (e.g. http://localhost:5000).
        // Left unset, the URI is derived from the current request, which behind the Kemp VIP
        // sends this WebSocket back out of the node and through TLS termination just to reach
        // a hub in the same process - one wasted round trip per circuit. Cross-node fan-out
        // is unaffected either way: that happens over the Garnet backplane (ADR 08), not here.
        var hubBaseUrl = configuration["Realtime:HubBaseUrl"];
        var hubUri = string.IsNullOrWhiteSpace(hubBaseUrl)
            ? navigationManager.ToAbsoluteUri("/hubs/bids")
            : new Uri(new Uri(hubBaseUrl), "/hubs/bids");

        _connection = new HubConnectionBuilder()
            .WithUrl(hubUri)
            .WithAutomaticReconnect()
            .Build();
        _connection.On<int, decimal>("BidPlaced", (equipmentId, currentBid) => BidPlaced?.Invoke(equipmentId, currentBid));
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
        => _connection.StartAsync(cancellationToken);

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
