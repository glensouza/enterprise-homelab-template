using BrewHouse.Data;
using Wolverine;
using Wolverine.AmazonSqs;
using Wolverine.AzureServiceBus;
using Wolverine.CritterWatch;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;
using Wolverine.RabbitMQ.Internal;

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

        // docs/01 ADR 91 - required for CritterWatch's Pause/Restart admin
        // actions (leader-owned agent assignment changes); already Wolverine's
        // own framework default (confirmed live via ilspycmd against the
        // pinned 6.21.0 package - DurabilitySettings.Mode defaults to
        // Balanced), set explicitly so the requirement is visible in code
        // rather than relying on an implicit default that could change.
        options.Durability.Mode = DurabilityMode.Balanced;
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
                // docs/01 ADR 127: no RabbitMQ dead-letter exchange anywhere. A message that exhausts its retries is parked
                // in Wolverine's own database table (wolverine.wolverine_dead_letters), which is what the admin page
                // (ADR 125) lists, replays and discards. With the native exchange it went to
                // wolverine-dead-letter-queue instead, invisible to the page.
                options.UseRabbitMq(new Uri(settings.RabbitMqConnectionString)).AutoProvision().DisableDeadLetterQueueing();
                options.PublishMessage<ProcessBidMessage>().ToRabbitQueue("bids");
                options.ListenToRabbitQueue("bids");

                // docs/01 ADR 91 - CritterWatch (JasperFx's Wolverine/Marten
                // monitoring console). Reports over the same RabbitMQ
                // transport just configured above, not a second connection -
                // "critterwatch"/"brewhouse-control" are the queue names the
                // console's own Program.cs (src/CritterWatch) listens on.
                // Free tier (no JasperFx:LicenseKey configured) is read-only
                // monitoring; this wiring works either way.
                options.AddCritterWatchMonitoring(
                    critterWatchUri: new Uri("rabbitmq://queue/critterwatch"),
                    systemControlUri: new Uri("rabbitmq://queue/brewhouse-control"));

                // docs/01 ADR 124: telemetry is only useful while it is fresh (ServiceUpdates carry a ~45 s
                // expiry). With the transport-wide dead-letter exchange on this queue, every expired message
                // piled up in wolverine-dead-letter-queue whenever the console was down or not yet deployed
                // (1,500+ in 40 minutes after a rebuild). No dead-letter exchange: an expired status update
                // is simply dropped. The console's own listener on this queue must declare it the same way
                // (src/CritterWatch/Program.cs) - RabbitMQ rejects a redeclare with different arguments.
                options.Transports.GetOrCreate<RabbitMqTransport>().Queues["critterwatch"].DeadLetterQueue = null;
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
