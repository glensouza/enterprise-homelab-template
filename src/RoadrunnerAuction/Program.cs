using Amazon.Runtime;
using Amazon.S3;
using JasperFx.Resources;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using RoadrunnerAuction.Data;
using RoadrunnerAuction.Services;
using RoadrunnerAuction.Storage;
using StackExchange.Redis;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);

// 1. CONFIGURATION: Fail fast in Production when secrets were not injected.
//    - Local dev: Aspire AppHost injects ConnectionStrings__* dynamically.
//    - Production: the Infisical Agent renders /etc/roadrunner/roadrunner.env,
//      loaded by systemd via EnvironmentFile= (see src/systemd/blazor-app.service).
string RequireConnectionString(string name)
{
    var value = builder.Configuration.GetConnectionString(name);
    if (string.IsNullOrWhiteSpace(value))
    {
        if (builder.Environment.IsDevelopment())
            throw new InvalidOperationException(
                $"Connection string '{name}' is missing. Run the app via the Aspire AppHost " +
                "(src/RoadrunnerAuction.AppHost) which provisions and wires local dependencies.");
        throw new InvalidOperationException(
            $"Connection string '{name}' is missing. Ensure /etc/roadrunner/roadrunner.env " +
            "(rendered by the Infisical Agent) defines ConnectionStrings__" + name + ".");
    }
    return value;
}

var dbConnectionString = RequireConnectionString("roadrunnerdb");
var cacheConnectionString = RequireConnectionString("cache");

// Messaging:Transport selects the Wolverine broker (rabbitmq | sqs | servicebus,
// default rabbitmq). ConnectionStrings:messaging (RabbitMQ AMQP URI) is only
// required when the rabbitmq transport is selected.
var messagingTransport = builder.Configuration["Messaging:Transport"] ?? MessagingTransportConfigurator.RabbitMq;
var rabbitConnectionString = messagingTransport == MessagingTransportConfigurator.RabbitMq
    ? RequireConnectionString("messaging")
    : null;

// 2. OBSERVABILITY: OpenTelemetry logs, metrics, and traces.
//    OTLP endpoint comes from OTEL_EXPORTER_OTLP_ENDPOINT (Aspire Dashboard locally,
//    Grafana Alloy in production). No-op exporter when the endpoint is unset.
builder.Logging.AddOpenTelemetry(options =>
{
    options.IncludeFormattedMessage = true;
    options.IncludeScopes = true;
});
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation())
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource("Npgsql"))
    .UseOtlpExporter();

// 3. DATABASE
//    Scoped context for short-lived operations (Wolverine handlers) and a factory
//    for Blazor Server components (long-lived circuits must not hold a scoped context).
builder.Services.AddDbContext<AuctionDbContext>(options =>
    options.UseNpgsql(dbConnectionString));
builder.Services.AddDbContextFactory<AuctionDbContext>(options =>
    options.UseNpgsql(dbConnectionString));

// 4. CACHE & SIGNALR BACKPLANE (Garnet, RESP-compatible)
//    BidsHub rides this same backplane, so a bid placed on Web 01 fans out to
//    every circuit connected to Web 02 (ADR 08) - see LiveBids.razor.
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(cacheConnectionString));
builder.Services.AddSignalR().AddStackExchangeRedis(cacheConnectionString);
builder.Services.AddScoped<IBidsClient, SignalRBidsClient>();

// 5. STORAGE: app-owned IBlobStore abstraction (ADR 03). BlobStorage:Provider
//    selects the implementation - "local" (default, Synology NAS mount) or
//    "s3" (AWSSDK.S3 - real Amazon S3, or any S3-compatible endpoint such as
//    the Floci emulator in the PR preview stack via BlobStorage:S3:ServiceUrl).
var blobStorageProvider = builder.Configuration["BlobStorage:Provider"] ?? "local";
switch (blobStorageProvider)
{
    case "s3":
        var s3ServiceUrl = builder.Configuration["BlobStorage:S3:ServiceUrl"];
        var s3BucketName = builder.Configuration["BlobStorage:S3:BucketName"] ?? "roadrunner-auction-blobs";
        var s3Config = new AmazonS3Config
        {
            ForcePathStyle = builder.Configuration.GetValue("BlobStorage:S3:ForcePathStyle", true),
        };
        if (!string.IsNullOrWhiteSpace(s3ServiceUrl))
            s3Config.ServiceURL = s3ServiceUrl;
        else
            s3Config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(builder.Configuration["BlobStorage:S3:Region"] ?? "us-east-1");

        var s3AccessKey = builder.Configuration["BlobStorage:S3:AccessKey"];
        var s3SecretKey = builder.Configuration["BlobStorage:S3:SecretKey"];
        builder.Services.AddSingleton<IBlobStore>(_ =>
        {
            IAmazonS3 s3Client = string.IsNullOrWhiteSpace(s3AccessKey)
                ? new AmazonS3Client(s3Config) // real S3: default AWS credential chain (IAM role, env, profile)
                : new AmazonS3Client(new BasicAWSCredentials(s3AccessKey, s3SecretKey), s3Config);
            return new S3BlobStore(s3Client, s3BucketName);
        });
        break;
    case "local":
    default:
        builder.Services.AddSingleton<IBlobStore>(_ =>
            new LocalDiskBlobStore(builder.Configuration["BlobStorage:RootPath"] ?? "./data/blobs"));
        break;
}

// 6. MESSAGING: Wolverine. Messaging:Transport (rabbitmq | sqs | servicebus)
//    selects the broker purely via configuration - handlers (ProcessBidHandler)
//    never change (ADR 07). See MessagingTransportConfigurator.
var messagingSettings = new MessagingTransportSettings
{
    Transport = messagingTransport,
    RabbitMqConnectionString = rabbitConnectionString,
    SqsServiceUrl = builder.Configuration["Messaging:Sqs:ServiceUrl"],
    SqsRegion = builder.Configuration["Messaging:Sqs:Region"],
    ServiceBusConnectionString = builder.Configuration["Messaging:ServiceBus:ConnectionString"],
};
builder.Host.UseWolverine(options =>
{
    MessagingTransportConfigurator.ConfigureDurability(options, dbConnectionString);
    MessagingTransportConfigurator.Configure(options, messagingSettings);
});
// Local dev only: auto-creates Wolverine's own "wolverine" schema (envelope
// storage) on startup for convenience. Production applies it as a deliberate,
// once-per-deploy step (`dotnet <app>.dll db-apply`) - never on concurrent
// multi-node boot, for the same race-condition reason ADR 11 forbids
// Database.Migrate() on boot for the app's own EF Core schema.
if (builder.Environment.IsDevelopment())
{
    builder.Host.UseResourceSetupOnStartup();
}

// 7. DEEP HEALTH CHECKS (For Kemp L7)
var healthChecksBuilder = builder.Services.AddHealthChecks()
    .AddNpgSql(dbConnectionString)
    .AddRedis(cacheConnectionString);
if (messagingTransport == MessagingTransportConfigurator.RabbitMq)
{
    healthChecksBuilder.AddRabbitMQ(_ =>
        new RabbitMQ.Client.ConnectionFactory { Uri = new Uri(rabbitConnectionString!) }.CreateConnectionAsync());
}

// 8. VERSION: exposed via VersionService (reads assembly version injected at publish by /p:Version)
builder.Services.AddSingleton<VersionService>();

// 9. BLAZOR WEBSOCKET TIMEOUTS
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddServerSideBlazor(options =>
{
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(3);
})
.AddHubOptions(options =>
{
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.HandshakeTimeout = TimeSpan.FromSeconds(30);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseAntiforgery();
app.MapHealthChecks("/health"); // Kemp probes this endpoint
app.MapHub<RoadrunnerAuction.Hubs.BidsHub>("/hubs/bids");
app.MapRazorComponents<RoadrunnerAuction.Components.App>().AddInteractiveServerRenderMode();

app.Run();
