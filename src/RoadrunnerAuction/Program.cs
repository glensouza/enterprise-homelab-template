using Microsoft.EntityFrameworkCore;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using RoadrunnerAuction.Data;
using RoadrunnerAuction.Storage;
using StackExchange.Redis;
using Wolverine;
using Wolverine.RabbitMQ;

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
var rabbitConnectionString = RequireConnectionString("messaging");

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
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(cacheConnectionString));
builder.Services.AddSignalR().AddStackExchangeRedis(cacheConnectionString);

// 5. STORAGE: app-owned IBlobStore abstraction (ADR 03). Swap the registration
//    to an Azure Blob / S3 / GCS adapter when moving off the Synology NAS mount.
builder.Services.AddSingleton<IBlobStore>(_ =>
    new LocalDiskBlobStore(builder.Configuration["BlobStorage:RootPath"] ?? "./data/blobs"));

// 6. MESSAGING: Wolverine over RabbitMQ. Transport-agnostic - swap
//    UseRabbitMq for Azure Service Bus / SQS via config when migrating cloud.
builder.Host.UseWolverine(options =>
{
    options.UseRabbitMq(new Uri(rabbitConnectionString)).AutoProvision();
    options.PublishMessage<ProcessBidMessage>().ToRabbitQueue("bids");
    options.ListenToRabbitQueue("bids");
});

// 7. DEEP HEALTH CHECKS (For Kemp L7)
builder.Services.AddHealthChecks()
    .AddNpgSql(dbConnectionString)
    .AddRedis(cacheConnectionString)
    .AddRabbitMQ(_ => new RabbitMQ.Client.ConnectionFactory { Uri = new Uri(rabbitConnectionString) }.CreateConnectionAsync());

// 8. BLAZOR WEBSOCKET TIMEOUTS
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
app.MapRazorComponents<RoadrunnerAuction.Components.App>().AddInteractiveServerRenderMode();

app.Run();
