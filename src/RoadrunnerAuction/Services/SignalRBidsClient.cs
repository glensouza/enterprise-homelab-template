using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace RoadrunnerAuction.Services;

/// <inheritdoc cref="IBidsClient" />
public class SignalRBidsClient : IBidsClient
{
    private readonly HubConnection _connection;

    public event Action<int, decimal>? BidPlaced;

    public SignalRBidsClient(NavigationManager navigationManager)
    {
        _connection = new HubConnectionBuilder()
            .WithUrl(navigationManager.ToAbsoluteUri("/hubs/bids"))
            .WithAutomaticReconnect()
            .Build();
        _connection.On<int, decimal>("BidPlaced", (equipmentId, currentBid) => BidPlaced?.Invoke(equipmentId, currentBid));
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
        => _connection.StartAsync(cancellationToken);

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
