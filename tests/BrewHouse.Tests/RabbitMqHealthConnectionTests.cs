using BrewHouse.Services;
using Moq;
using RabbitMQ.Client;
using Xunit;

namespace BrewHouse.Tests;

/// <summary>
/// Regression tests for the /health connection leak (docs/01 ADR 108): every
/// probe used to open a fresh, never-disposed AMQP connection. No broker needed.
/// </summary>
public class RabbitMqHealthConnectionTests
{
    [Fact]
    public async Task GetConnectionAsync_Reuses_One_Connection_Across_Many_Probes()
    {
        var created = 0;
        var connection = new Mock<IConnection>();
        connection.SetupGet(c => c.IsOpen).Returns(true);
        await using var holder = new RabbitMqHealthConnection(_ =>
        {
            created++;
            return Task.FromResult(connection.Object);
        });

        for (var i = 0; i < 100; i++)
        {
            Assert.Same(connection.Object, await holder.GetConnectionAsync());
        }

        Assert.Equal(1, created);
    }

    [Fact]
    public async Task GetConnectionAsync_Disposes_A_Closed_Connection_Before_Replacing_It()
    {
        var first = new Mock<IConnection>();
        first.SetupGet(c => c.IsOpen).Returns(false);
        var second = new Mock<IConnection>();
        second.SetupGet(c => c.IsOpen).Returns(true);
        var queue = new Queue<IConnection>([first.Object, second.Object]);
        await using var holder = new RabbitMqHealthConnection(_ => Task.FromResult(queue.Dequeue()));

        // First call returns the (already closed) first connection; the next one must
        // dispose it rather than abandon it, then hand back the replacement.
        Assert.Same(first.Object, await holder.GetConnectionAsync());
        Assert.Same(second.Object, await holder.GetConnectionAsync());

        first.Verify(c => c.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task GetConnectionAsync_Does_Not_Cache_A_Failed_Connect()
    {
        var attempts = 0;
        var connection = new Mock<IConnection>();
        connection.SetupGet(c => c.IsOpen).Returns(true);
        await using var holder = new RabbitMqHealthConnection(_ =>
            ++attempts == 1
                ? throw new InvalidOperationException("broker down")
                : Task.FromResult(connection.Object));

        await Assert.ThrowsAsync<InvalidOperationException>(() => holder.GetConnectionAsync());

        // Broker came back: the next probe must retry, not replay the cached failure.
        Assert.Same(connection.Object, await holder.GetConnectionAsync());
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task DisposeAsync_Disposes_The_Live_Connection()
    {
        var connection = new Mock<IConnection>();
        connection.SetupGet(c => c.IsOpen).Returns(true);
        var holder = new RabbitMqHealthConnection(_ => Task.FromResult(connection.Object));
        await holder.GetConnectionAsync();

        await holder.DisposeAsync();

        connection.Verify(c => c.DisposeAsync(), Times.Once);
    }
}
