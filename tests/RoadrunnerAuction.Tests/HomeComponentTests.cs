using Bunit;
using FluentStorage.Blobs;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using StackExchange.Redis;
using RoadrunnerAuction.Components.Pages;
using RoadrunnerAuction.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace RoadrunnerAuction.Tests;
public class HomeComponentTests : TestContext {
    [Fact]
    public void Click_UploadPhoto_Updates_UI_Status() {
        var mockStorage = new Mock<IBlobStorage>();
        var mockBus = new Mock<IBus>();
        var mockRedis = new Mock<IConnectionMultiplexer>();
        var options = new DbContextOptionsBuilder<AuctionDbContext>()
            .UseInMemoryDatabase(databaseName: "Test_DB").Options;
        var dbContext = new AuctionDbContext(options);
        Services.AddSingleton(mockStorage.Object);
        Services.AddSingleton(mockBus.Object);
        Services.AddSingleton(mockRedis.Object);
        Services.AddSingleton(dbContext);
        var cut = RenderComponent<Home>();
        cut.Find("button:contains('Simulate Photo Upload')").Click();
        Assert.Contains("Mock photo written to storage backend successfully.", cut.Markup);
    }
}
