using Microsoft.AspNetCore.SignalR;

namespace RoadrunnerAuction.Hubs;

/// <summary>
/// Broadcast-only hub for live bid updates. Clients never call server methods on
/// it directly - ProcessBidHandler pushes "BidPlaced" to Clients.All after saving
/// a bid. Registered on the same AddSignalR().AddStackExchangeRedis(...) backplane
/// as the Blazor circuit hub (ADR 08), so a bid placed against Web 01 fans out to
/// every circuit connected to Web 02 through Garnet pub/sub.
/// </summary>
public class BidsHub : Hub;
