using RoadrunnerAuction.Data;
using Wolverine;
using Wolverine.AmazonSqs;
using Wolverine.AzureServiceBus;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;

namespace RoadrunnerAuction.Services;

/// <summary>
/// Selects and configures the Wolverine transport for the "bids" queue based on
/// Messaging:Transport (rabbitmq | sqs | servicebus, default rabbitmq). Handlers
/// (ProcessBidHandler) are transport-agnostic and never change (ADR 07) - only the
/// broker connection and endpoint routing differ.
/// </summary>
public static class MessagingTransportConfigurator
{
    public const string RabbitMq = "rabbitmq";
    public const string Sqs = "sqs";
    public const string ServiceBus = "servicebus";

    /// <summary>
    /// Durable outbox/inbox (ADR 07): every message is written to Postgres in the
    /// same transaction as the business change before it ever touches the broker.
    /// If the broker is unreachable the message sits in wolverine.wolverine_outgoing_envelopes
    /// and is retried automatically once it recovers - nothing is lost. Applies
    /// uniformly regardless of which transport is selected.
    /// </summary>
    public static void ConfigureDurability(WolverineOptions options, string dbConnectionString)
    {
        options.PersistMessagesWithPostgresql(dbConnectionString, "wolverine");
        options.Policies.UseDurableOutboxOnAllSendingEndpoints();
        options.Policies.UseDurableInboxOnAllListeners();
    }

    public static void Configure(WolverineOptions options, MessagingTransportSettings settings)
    {
        switch (settings.Transport)
        {
            case Sqs:
                options.UseAmazonSqsTransport(sqs =>
                {
                    if (!string.IsNullOrWhiteSpace(settings.SqsServiceUrl))
                        sqs.ServiceURL = settings.SqsServiceUrl;
                    else
                        sqs.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(settings.SqsRegion ?? "us-east-1");
                }).AutoProvision();
                options.PublishMessage<ProcessBidMessage>().ToSqsQueue("bids");
                options.ListenToSqsQueue("bids");
                break;

            case ServiceBus:
                if (string.IsNullOrWhiteSpace(settings.ServiceBusConnectionString))
                    throw new InvalidOperationException(
                        "Messaging:ServiceBus:ConnectionString is required when Messaging:Transport is 'servicebus'.");
                options.UseAzureServiceBus(settings.ServiceBusConnectionString).AutoProvision();
                options.PublishMessage<ProcessBidMessage>().ToAzureServiceBusQueue("bids");
                options.ListenToAzureServiceBusQueue("bids");
                break;

            case RabbitMq:
            default:
                if (string.IsNullOrWhiteSpace(settings.RabbitMqConnectionString))
                    throw new InvalidOperationException(
                        "ConnectionStrings:messaging is required when Messaging:Transport is 'rabbitmq'.");
                options.UseRabbitMq(new Uri(settings.RabbitMqConnectionString)).AutoProvision();
                options.PublishMessage<ProcessBidMessage>().ToRabbitQueue("bids");
                options.ListenToRabbitQueue("bids");
                break;
        }
    }
}

public record MessagingTransportSettings
{
    public string Transport { get; init; } = MessagingTransportConfigurator.RabbitMq;
    public string? RabbitMqConnectionString { get; init; }
    public string? SqsServiceUrl { get; init; }
    public string? SqsRegion { get; init; }
    public string? ServiceBusConnectionString { get; init; }
}
