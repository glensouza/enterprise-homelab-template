using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using RoadrunnerAuction.Services;
using RoadrunnerAuction.Data;
using Xunit;
namespace RoadrunnerAuction.Tests;
public class BidConsumerTests {
    [Fact]
    public async Task Consumer_Successfully_Consumes_Bid_Message() {
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x => { x.AddConsumer<BidProcessingConsumer>(); })
            .BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        await harness.Bus.Publish(new ProcessBidMessage { EquipmentId = 999, BidAmount = 50000m });
        Assert.True(await harness.Consumed.Any<ProcessBidMessage>());
    }
}
