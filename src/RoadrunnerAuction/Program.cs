using Amazon.Runtime;
using Amazon.S3;
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
app.MapRazorComponents<RoadrunnerAuction.Components.App>().AddInteractiveServerRenderMode();

app.Run();
