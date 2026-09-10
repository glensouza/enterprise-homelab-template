using Aspire.Hosting.Testing;
using JasperFx.Resources;
using Microsoft.Extensions.Hosting;
using Npgsql;
using BrewHouse.Services;
using Wolverine;
using Xunit;

namespace BrewHouse.Tests;

/// <summary>
/// Integration test against the real Aspire-orchestrated PostgreSQL container.
/// Requires Docker to be running. Proves MessagingTransportConfigurator.ConfigureDurability
/// actually provisions Wolverine's durable outbox/inbox schema in Postgres (ADR 07),
/// rather than only asserting on in-memory WolverineOptions state.
/// </summary>
public class WolverineDurabilityIntegrationTests
{
    [Fact]
    public async Task ConfigureDurability_Provisions_Envelope_Storage_In_Postgres()
    {
        var appHostBuilder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.BrewHouse_AppHost>();
        await using var appHost = await appHostBuilder.BuildAsync();
        await appHost.StartAsync();

        var connectionString = await appHost.GetConnectionStringAsync("brewhousedb");
        Assert.False(string.IsNullOrWhiteSpace(connectionString));

        using var wolverineHost = await Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                MessagingTransportConfigurator.ConfigureDurability(options, connectionString!);
            })
            .UseResourceSetupOnStartup()
            .StartAsync();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        // ::text matters - to_regclass returns PostgreSQL's `regclass` OID type, which
        // Npgsql has no CLR mapping for and throws on when read back.
        command.CommandText = "SELECT to_regclass('wolverine.wolverine_outgoing_envelopes')::text";
        var envelopeTable = await command.ExecuteScalarAsync();

        Assert.Equal("wolverine.wolverine_outgoing_envelopes", envelopeTable);

        await wolverineHost.StopAsync();
    }
}
