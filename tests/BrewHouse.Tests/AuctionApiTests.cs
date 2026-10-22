using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using BrewHouse.Data;
using BrewHouse.Services;
using Wolverine;
using Xunit;

namespace BrewHouse.Tests;

public class AuctionApiTests
{
    private static async Task<HttpClient> CreateClient(ICoffeeLotReader reader, IMessageBus bus)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuctionVersioning();
        builder.Services.AddSingleton(reader);
        builder.Services.AddSingleton(bus);
        var app = builder.Build();
        app.MapAuctionApi();
        app.MapAuctionDocs();
        await app.StartAsync();
        return app.GetTestClient();
    }

    private static ICoffeeLotReader ReaderWith(params CoffeeLotSnapshot[] lots)
    {
        var reader = new Mock<ICoffeeLotReader>();
        reader.Setup(r => r.GetLotsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(lots);
        return reader.Object;
    }

    [Fact]
    public async Task V1_List_Uses_CurrentBid_And_V2_Renames_It_To_Bid()
    {
        var bus = new Mock<IMessageBus>().Object;
        var client = await CreateClient(
            ReaderWith(new CoffeeLotSnapshot { Id = 4, Origin = "Terraform Roast", CurrentBid = 450m }),
            bus);

        var v1 = await client.GetFromJsonAsync<JsonElement>("/api/v1/auctions");
        var v2 = await client.GetFromJsonAsync<JsonElement>("/api/v2/auctions");

        Assert.Equal(450m, v1[0].GetProperty("currentBid").GetDecimal());
        Assert.False(v1[0].TryGetProperty("bid", out _));
        Assert.Equal(450m, v2[0].GetProperty("bid").GetDecimal());
        Assert.False(v2[0].TryGetProperty("currentBid", out _));
    }

    [Fact]
    public async Task Post_Bid_Publishes_The_Same_Message_The_Button_Uses()
    {
        var bus = new Mock<IMessageBus>();
        var client = await CreateClient(
            ReaderWith(new CoffeeLotSnapshot { Id = 4, Origin = "Terraform Roast", CurrentBid = 450m }),
            bus.Object);

        var response = await client.PostAsJsonAsync("/api/v1/auctions/4/bids", new PlaceBidRequest { Amount = 950m });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        bus.Verify(
            b => b.PublishAsync(
                It.Is<ProcessBidMessage>(m => m.CoffeeLotId == 4 && m.BidAmount == 950m),
                It.IsAny<DeliveryOptions>()),
            Times.Once);
    }

    [Fact]
    public async Task Post_Bid_That_Does_Not_Beat_The_Standing_Price_Is_Conflict()
    {
        var bus = new Mock<IMessageBus>();
        var client = await CreateClient(
            ReaderWith(new CoffeeLotSnapshot { Id = 4, Origin = "Terraform Roast", CurrentBid = 450m }),
            bus.Object);

        var response = await client.PostAsJsonAsync("/api/v1/auctions/4/bids", new PlaceBidRequest { Amount = 450m });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        bus.Verify(
            b => b.PublishAsync(It.IsAny<ProcessBidMessage>(), It.IsAny<DeliveryOptions>()),
            Times.Never);
    }

    [Fact]
    public async Task OpenApi_Document_Lists_Both_Versions()
    {
        var client = await CreateClient(ReaderWith(), new Mock<IMessageBus>().Object);
        var v1 = await client.GetStringAsync("/openapi/v1.json");
        var v2 = await client.GetStringAsync("/openapi/v2.json");

        Assert.Contains("/api/v1/auctions/{id}/bids", v1);
        Assert.DoesNotContain("/api/v2/", v1);
        Assert.Contains("/api/v2/auctions", v2);
        Assert.DoesNotContain("/bids", v2);
    }
}
