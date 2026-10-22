using System.Reflection;
using StackExchange.Redis;
using StackExchange.Redis.Profiling;

namespace BrewHouse.Services;

/// <summary>
/// An <see cref="IConnectionMultiplexer"/> that does not open a socket until something
/// actually uses it. OpenTelemetry's Redis instrumentation calls <see cref="RegisterProfiler"/>
/// while the host is starting, and <c>dotnet BrewHouse.dll db-apply</c> / <c>codegen test</c>
/// must keep working when Garnet is down (ADR 131). The real <see cref="ConnectionMultiplexer.Connect(string)"/>
/// runs on the first command.
/// </summary>
public class LazyConnectionMultiplexer : DispatchProxy
{
    private readonly object _gate = new();
    private string _connectionString = "";
    private Func<ProfilingSession?>? _profiler;
    private IConnectionMultiplexer? _inner;

    public static IConnectionMultiplexer Create(string connectionString)
    {
        var proxy = Create<IConnectionMultiplexer, LazyConnectionMultiplexer>();
        var typed = (LazyConnectionMultiplexer)(object)proxy;
        typed._connectionString = connectionString;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        if (targetMethod.Name == nameof(IConnectionMultiplexer.RegisterProfiler))
        {
            _profiler = (Func<ProfilingSession?>?)args?[0];
            if (_inner is not null && _profiler is not null)
                _inner.RegisterProfiler(_profiler);
            return null;
        }

        if (_inner is null && targetMethod.Name is "Dispose" or "DisposeAsync" or "Close" or "CloseAsync")
            return Completed(targetMethod.ReturnType);

        if (_inner is null && targetMethod.Name == "get_IsConnected")
            return false;
        if (_inner is null && targetMethod.Name == "get_IsConnecting")
            return false;
        if (_inner is null && targetMethod.Name == "get_Configuration")
            return _connectionString;

        try
        {
            return targetMethod.Invoke(Connect(), args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private IConnectionMultiplexer Connect()
    {
        if (_inner is not null)
            return _inner;

        lock (_gate)
        {
            if (_inner is not null)
                return _inner;

            var connection = ConnectionMultiplexer.Connect(_connectionString);
            if (_profiler is not null)
                connection.RegisterProfiler(_profiler);
            _inner = connection;
            return connection;
        }
    }

    private static object? Completed(Type returnType)
    {
        if (returnType == typeof(ValueTask))
            return ValueTask.CompletedTask;
        if (returnType == typeof(Task))
            return Task.CompletedTask;
        return null;
    }
}
