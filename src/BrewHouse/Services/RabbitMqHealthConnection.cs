using RabbitMQ.Client;

namespace BrewHouse.Services;

/// <summary>
/// One long-lived AMQP connection shared by every /health poll (docs/01 ADR 108).
/// AspNetCore.HealthChecks.Rabbitmq invokes its connection factory on EVERY check
/// and never disposes what it returns, so the previous per-call
/// <c>CreateConnectionAsync()</c> leaked one connection per probe - Kemp polls
/// every 9s, which exhausted RabbitMQ's file descriptors in about a day. A closed
/// connection is disposed before being replaced, so an outage can't leak either.
/// </summary>
public sealed class RabbitMqHealthConnection(Func<CancellationToken, Task<IConnection>> connectAsync)
    : IAsyncDisposable
{
    private readonly Func<CancellationToken, Task<IConnection>> _connectAsync = connectAsync;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    public async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is { IsOpen: true } open)
        {
            return open;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true } reopened)
            {
                return reopened;
            }

            if (_connection is not null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }

            _connection = await _connectAsync(cancellationToken);
            return _connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }

        _gate.Dispose();
    }
}
