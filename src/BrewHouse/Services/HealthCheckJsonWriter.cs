using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BrewHouse.Services;

/// <summary>
/// JSON body for <c>/health/detail</c>. <c>/health</c> stays the one-word payload Kemp probes.
/// </summary>
public static class HealthCheckJsonWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var body = new
        {
            status = report.Status.ToString(),
            results = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    status = entry.Value.Status.ToString(),
                    description = entry.Value.Description,
                }),
        };
        return context.Response.WriteAsJsonAsync(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
}
