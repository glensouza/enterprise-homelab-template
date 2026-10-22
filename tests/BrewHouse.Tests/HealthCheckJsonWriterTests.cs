using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using BrewHouse.Services;
using Xunit;

namespace BrewHouse.Tests;

public class HealthCheckJsonWriterTests
{
    [Fact]
    public async Task Write_Lists_Each_Dependency()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>
            {
                ["npgsql"] = new(HealthStatus.Healthy, "ready", TimeSpan.Zero, exception: null, data: null),
                ["redis"] = new(HealthStatus.Healthy, "ready", TimeSpan.Zero, exception: null, data: null),
                ["rabbitmq"] = new(HealthStatus.Unhealthy, "down", TimeSpan.Zero, exception: null, data: null),
            },
            TimeSpan.Zero);

        await HealthCheckJsonWriter.WriteAsync(context, report);

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.StartsWith("application/json", context.Response.ContentType);
        Assert.Equal("Unhealthy", document.RootElement.GetProperty("status").GetString());
        Assert.Equal("Healthy", document.RootElement.GetProperty("results").GetProperty("npgsql").GetProperty("status").GetString());
        Assert.Equal("Healthy", document.RootElement.GetProperty("results").GetProperty("redis").GetProperty("status").GetString());
        Assert.Equal("Unhealthy", document.RootElement.GetProperty("results").GetProperty("rabbitmq").GetProperty("status").GetString());
    }
}
