using Aspire.Hosting.Testing;
using JasperFx.Resources;
using Microsoft.Extensions.Hosting;
using Npgsql;
using RoadrunnerAuction.Services;
using Wolverine;
using Xunit;

namespace RoadrunnerAuction.Tests;

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
            .CreateAsync<Projects.RoadrunnerAuction_AppHost>();
        await using var appHost = await appHostBuilder.BuildAsync();
        await appHost.StartAsync();

        var connectionString = await appHost.GetConnectionStringAsync("roadrunnerdb");
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
        command.CommandText = "SELECT to_regclass('wolverine.wolverine_outgoing_envelopes')";
        var envelopeTable = await command.ExecuteScalarAsync();

        Assert.NotNull(envelopeTable);
        Assert.NotEqual(DBNull.Value, envelopeTable);

        await wolverineHost.StopAsync();
    }
}
