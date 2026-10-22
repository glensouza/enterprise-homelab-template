using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using BrewHouse.Components;
using BrewHouse.Data;
using BrewHouse.Services;
using Wolverine;
using Xunit;

namespace BrewHouse.Tests;

public class LiveBidsComponentTests : BunitContext
{
    private static Mock<IBidsClient> CreateBidsClientMock()
    {
        var mockBidsClient = new Mock<IBidsClient>();
        mockBidsClient.Setup(c => c.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mockBidsClient.Setup(c => c.DisposeAsync()).Returns(ValueTask.CompletedTask);
        return mockBidsClient;
    }

    private static Mock<ICoffeeLotReader> CreateReader(params CoffeeLotSnapshot[] lots)
    {
        var reader = new Mock<ICoffeeLotReader>();
        reader.Setup(r => r.GetLotsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(lots);
        return reader;
    }

    private static void RegisterSimilar(BunitContext context, CoffeeLotSnapshot? nearest = null)
    {
        var similar = new Mock<ISimilarLotQuery>();
        similar.Setup(s => s.FindNearestAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(nearest);
        context.Services.AddSingleton(similar.Object);
    }

    [Fact]
    public void PlaceBid_Click_Publishes_ProcessBidMessage()
    {
        var mockMessageBus = new Mock<IMessageBus>();
        Services.AddSingleton(mockMessageBus.Object);
        Services.AddSingleton(CreateReader(new CoffeeLotSnapshot { Id = 1, Origin = "Ethiopia Yirgacheffe", CurrentBid = 1000m }).Object);
        Services.AddSingleton(CreateBidsClientMock().Object);
        RegisterSimilar(this);

        var cut = Render<LiveBids>();
        cut.WaitForState(() => cut.Markup.Contains("Ethiopia Yirgacheffe"));

        cut.Find("button").Click();

        mockMessageBus.Verify(
            b => b.PublishAsync(
                It.Is<ProcessBidMessage>(m => m.CoffeeLotId == 1 && m.BidAmount == 1500m),
                It.IsAny<DeliveryOptions>()),
            Times.Once);
    }

    [Fact]
    public void BidPlaced_Broadcast_Updates_Rendered_CurrentBid_Without_Publishing()
    {
        var mockMessageBus = new Mock<IMessageBus>();
        var mockBidsClient = CreateBidsClientMock();
        Services.AddSingleton(mockMessageBus.Object);
        Services.AddSingleton(CreateReader(new CoffeeLotSnapshot { Id = 1, Origin = "Ethiopia Yirgacheffe", CurrentBid = 1000m }).Object);
        Services.AddSingleton(mockBidsClient.Object);
        RegisterSimilar(this);

        var cut = Render<LiveBids>();
        cut.WaitForState(() => cut.Markup.Contains("Ethiopia Yirgacheffe"));

        mockBidsClient.Raise(c => c.BidPlaced += null, 1, 9999m);

        cut.WaitForState(() => cut.Find("[data-testid='bid-1']").TextContent.Contains("9,999"));
        mockMessageBus.Verify(b => b.PublishAsync(It.IsAny<ProcessBidMessage>(), It.IsAny<DeliveryOptions>()), Times.Never);
    }

    [Fact]
    public void Nearest_Lot_Is_Rendered_When_The_Query_Returns_One()
    {
        Services.AddSingleton(new Mock<IMessageBus>().Object);
        Services.AddSingleton(CreateReader(new CoffeeLotSnapshot { Id = 1, Origin = "Terraform Roast", CurrentBid = 450m }).Object);
        Services.AddSingleton(CreateBidsClientMock().Object);
        RegisterSimilar(this, new CoffeeLotSnapshot { Id = 2, Origin = "Wolverine Dark Roast", CurrentBid = 525m });

        var cut = Render<LiveBids>();

        cut.WaitForState(() => cut.Find("[data-testid='similar-lot']").TextContent.Contains("Wolverine Dark Roast"));
    }
}
