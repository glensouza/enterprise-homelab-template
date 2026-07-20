using MassTransit;
using RoadrunnerAuction.Data;
namespace RoadrunnerAuction.Services;
public class BidProcessingConsumer : IConsumer<ProcessBidMessage> {
    public async Task Consume(ConsumeContext<ProcessBidMessage> context) {
        await Task.Delay(50); 
    }
}
