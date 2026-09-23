using BrewHouse.Data;
using Wolverine;
using Wolverine.AmazonSqs;
using Wolverine.AzureServiceBus;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;

namespace BrewHouse.Services;

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
    /// Durable outbox/inbox (ADR 07): every outgoing message is persisted to Postgres
    /// (wolverine.wolverine_outgoing_envelopes) before it is handed to the broker, and
    /// retried automatically once the broker recovers - a broker outage delays delivery
    /// instead of losing messages. Applies uniformly regardless of transport.
    ///
    /// Note the exact guarantee: the envelope is written in Wolverine's OWN transaction,
    /// not enlisted in the caller's EF Core transaction. Making the send atomic with a
    /// business change additionally requires WolverineFx.EntityFrameworkCore and
    /// UseEntityFrameworkCoreTransactions(); nothing here publishes from inside a
    /// database transaction today, so that dependency is deliberately not taken on.
    /// </summary>
    public static void ConfigureDurability(WolverineOptions options, string dbConnectionString)
    {
        options.PersistMessagesWithPostgresql(dbConnectionString, "wolverine");
        options.Policies.UseDurableOutboxOnAllSendingEndpoints();
        options.Policies.UseDurableInboxOnAllListeners();

        // ProcessBidHandler.Handle takes AuctionDbContext directly. AddDbContext registers
        // DbContextOptions<AuctionDbContext> via an opaque lambda factory that Wolverine's
        // codegen can't compose inline, so it needs service location for this one type -
        // disallowed by default since Wolverine 6.0 (InvalidServiceLocationException,
        // confirmed live via `dotnet run -- codegen test`, which is what actually caught
        // this - every previous "it works" signal was bUnit tests calling Handle() as a
        // plain static method, bypassing Wolverine's generated dispatch pipeline entirely).
        options.CodeGeneration.AlwaysUseServiceLocationFor<AuctionDbContext>();
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
