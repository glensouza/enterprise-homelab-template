using BrewHouse.Services;
using Wolverine;
using Wolverine.AmazonSqs.Internal;
using Wolverine.AzureServiceBus;
using Wolverine.RabbitMQ;
using Wolverine.RabbitMQ.Internal;
using Xunit;

namespace BrewHouse.Tests;

/// <summary>
/// Direct method invocation against WolverineOptions, no broker required -
/// verifies Messaging:Transport selects the right Wolverine transport without
/// touching ProcessBidHandler (transport-agnostic by design, ADR 07).
/// </summary>
public class MessagingTransportConfiguratorTests
{
    [Fact]
    public void Configure_Rabbitmq_Registers_RabbitMqTransport()
    {
        var options = new WolverineOptions();

        MessagingTransportConfigurator.Configure(options, new MessagingTransportSettings
        {
            Transport = MessagingTransportConfigurator.RabbitMq,
            RabbitMqConnectionString = "amqp://guest:guest@localhost:5672",
        });

        Assert.Single(options.Transports.OfType<RabbitMqTransport>());
    }

    [Fact]
    public void Configure_Rabbitmq_Without_ConnectionString_Throws()
    {
        var options = new WolverineOptions();

        Assert.Throws<InvalidOperationException>(() =>
            MessagingTransportConfigurator.Configure(options, new MessagingTransportSettings
            {
                Transport = MessagingTransportConfigurator.RabbitMq,
                RabbitMqConnectionString = null,
            }));
    }

    [Fact]
    public void Configure_Sqs_Registers_AmazonSqsTransport()
    {
        var options = new WolverineOptions();

        MessagingTransportConfigurator.Configure(options, new MessagingTransportSettings
        {
            Transport = MessagingTransportConfigurator.Sqs,
            SqsServiceUrl = "http://localhost:4566",
        });

        Assert.Single(options.Transports.OfType<AmazonSqsTransport>());
    }

    [Fact]
    public void Configure_ServiceBus_Registers_AzureServiceBusTransport()
    {
        var options = new WolverineOptions();

        MessagingTransportConfigurator.Configure(options, new MessagingTransportSettings
        {
            Transport = MessagingTransportConfigurator.ServiceBus,
            ServiceBusConnectionString = "Endpoint=sb://example.servicebus.windows.net/;SharedAccessKeyName=x;SharedAccessKey=y",
        });

        Assert.Single(options.Transports.OfType<AzureServiceBusTransport>());
    }

    [Fact]
    public void Configure_ServiceBus_Without_ConnectionString_Throws()
    {
        var options = new WolverineOptions();

        Assert.Throws<InvalidOperationException>(() =>
            MessagingTransportConfigurator.Configure(options, new MessagingTransportSettings
            {
                Transport = MessagingTransportConfigurator.ServiceBus,
                ServiceBusConnectionString = null,
            }));
    }

    // docs/01 ADR 124: expired CritterWatch telemetry must be dropped, not dead-lettered.
    [Fact]
    public void Configure_Rabbitmq_CritterWatch_Queue_Has_No_DeadLetterQueue()
    {
        var options = new WolverineOptions();

        MessagingTransportConfigurator.Configure(options, new MessagingTransportSettings
        {
            Transport = MessagingTransportConfigurator.RabbitMq,
            RabbitMqConnectionString = "amqp://guest:guest@localhost:5672",
        });

        var transport = options.Transports.GetOrCreate<RabbitMqTransport>();
        Assert.Null(transport.Queues["critterwatch"].DeadLetterQueue);
    }

    // docs/01 ADR 127: failed bids must park in Wolverine's database dead-letter table (what the admin page lists),
    // not in RabbitMQ's wolverine-dead-letter-queue.
    [Fact]
    public void Configure_Rabbitmq_Uses_Wolverine_Database_Dead_Letters()
    {
        var options = new WolverineOptions();

        MessagingTransportConfigurator.Configure(options, new MessagingTransportSettings
        {
            Transport = MessagingTransportConfigurator.RabbitMq,
            RabbitMqConnectionString = "amqp://guest:guest@localhost:5672",
        });

        var transport = options.Transports.GetOrCreate<RabbitMqTransport>();
        Assert.Equal(DeadLetterQueueMode.WolverineStorage, transport.DeadLetterQueue.Mode);
    }
}
