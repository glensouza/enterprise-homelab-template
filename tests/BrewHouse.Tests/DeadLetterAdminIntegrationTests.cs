using Aspire.Hosting.Testing;
using BrewHouse.Services;
using JasperFx.Resources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.Runtime;
using Xunit;

namespace BrewHouse.Tests;

/// <summary>A message whose handler always throws - parks in the dead-letter store after one attempt.</summary>
public record AlwaysFailsMessage(string Reason);

public static class AlwaysFailsHandler
{
    public static void Handle(AlwaysFailsMessage message) => throw new InvalidOperationException(message.Reason);
}

/// <summary>
/// Integration test against the real Aspire-orchestrated PostgreSQL container (Docker required): a failing
/// handler parks a message in Wolverine's dead-letter table, and WolverineDeadLetterAdmin (the admin page's
/// backing service, docs/01 ADR 125) lists, replays and discards it.
/// </summary>
public class DeadLetterAdminIntegrationTests
{
    [Fact]
    public async Task Failed_Message_Can_Be_Listed_Replayed_And_Discarded()
    {
        var appHostBuilder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.BrewHouse_AppHost>();
        await using var appHost = await appHostBuilder.BuildAsync();
        await appHost.StartAsync();
        var connectionString = await appHost.GetConnectionStringAsync("brewhousedb");
        Assert.False(string.IsNullOrWhiteSpace(connectionString));

        using var host = await Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                MessagingTransportConfigurator.ConfigureDurability(options, connectionString!);
                options.Discovery.IncludeAssembly(typeof(AlwaysFailsHandler).Assembly);
                options.PublishMessage<AlwaysFailsMessage>().ToLocalQueue("always-fails").UseDurableInbox();
                options.OnException<InvalidOperationException>().MoveToErrorQueue();
            })
            .UseResourceSetupOnStartup()
            .StartAsync();

        var admin = new WolverineDeadLetterAdmin(host.Services.GetRequiredService<IWolverineRuntime>());
        var reason = $"boom-{Guid.NewGuid():N}";

        await host.Services.GetRequiredService<IMessageBus>().PublishAsync(new AlwaysFailsMessage(reason));

        DeadLetterItem? item = null;
        for (var attempt = 0; attempt < 60 && item is null; attempt++)
        {
            var page = await admin.ListAsync(1, 50);
            item = page.Items.FirstOrDefault(i => i.ExceptionMessage.Contains(reason));
            if (item is null)
                await Task.Delay(500);
        }

        Assert.NotNull(item);
        Assert.Contains("AlwaysFailsMessage", item!.MessageType);
        Assert.Contains("InvalidOperationException", item.ExceptionType);

        // Replay marks it replayable; the handler throws again, so it lands back in the store.
        await admin.ReplayAsync(item.Id);

        await admin.DiscardAsync(item.Id);
        var after = await admin.ListAsync(1, 50);
        Assert.DoesNotContain(after.Items, i => i.Id == item.Id);

        await host.StopAsync();
    }
}
