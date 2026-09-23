using System.Globalization;
using Amazon.Runtime;
using Amazon.S3;
using JasperFx;
using JasperFx.Resources;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using BrewHouse.Data;
using BrewHouse.Services;
using BrewHouse.Storage;
using StackExchange.Redis;
using Wolverine;

// Pin the default culture process-wide, before anything formats currency. This app is
// USD-only (see LiveBids.razor, ProcessBidHandler's log lines) but CurrentCulture on
// these Linux LXCs isn't guaranteed to resolve to en-US - confirmed live, ToString("C")
// was silently rendering the generic international currency sign (¤) instead of $ both
// in the UI and in journalctl. Fixing it here, once, beats hunting down every ":C" call
// site individually and catches any that get added later too.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("en-US");
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");

var builder = WebApplication.CreateBuilder(args);

// 1. CONFIGURATION: Fail fast in Production when secrets were not injected.
//    - Local dev: Aspire AppHost injects ConnectionStrings__* dynamically.
//    - Production: the Infisical Agent renders /etc/brewhouse/brewhouse.env,
//      loaded by systemd via EnvironmentFile= (see src/systemd/blazor-app.service).
string RequireConnectionString(string name)
{
    var value = builder.Configuration.GetConnectionString(name);
    if (string.IsNullOrWhiteSpace(value))
    {
        if (builder.Environment.IsDevelopment())
            throw new InvalidOperationException(
                $"Connection string '{name}' is missing. Run the app via the Aspire AppHost " +
                "(src/BrewHouse.AppHost) which provisions and wires local dependencies.");
        throw new InvalidOperationException(
            $"Connection string '{name}' is missing. Ensure /etc/brewhouse/brewhouse.env " +
            "(rendered by the Infisical Agent) defines ConnectionStrings__" + name + ".");
    }
    return value;
}

var dbConnectionString = RequireConnectionString("brewhousedb");
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
//    Grafana Alloy in the homelab). No-op exporter when the endpoint is unset.
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

// Data Protection keys, on the same Garnet instance as the cache/backplane
// above but its own lazy connection (matches AddStackExchangeRedis just
// above, which also dials its own connection rather than reusing the
// IConnectionMultiplexer singleton) - connects on first use, not at process
// startup, so CLI-only invocations (codegen test, db-apply) still don't
// require Garnet to be reachable. ASP.NET Core otherwise defaults to a
// machine-local key ring, so anything protected on one node - antiforgery
// tokens, and the persisted-component-state payload a Blazor Web App uses
// when a static-rendered page upgrades to an interactive circuit - can't be
// decrypted if the next request lands on the other node. Confirmed live:
// this was exactly what killed fresh circuits with "Circuit host not
// initialized" / "No Connection with that ID" whenever the initial page load
// and the SignalR negotiate landed on different nodes.
builder.Services.AddDataProtection()
    .PersistKeysToStackExchangeRedis(() => ConnectionMultiplexer.Connect(cacheConnectionString).GetDatabase(), "BrewHouse-DataProtection-Keys")
    .SetApplicationName("BrewHouse");

// 5. STORAGE: app-owned IBlobStore abstraction (ADR 03). BlobStorage:Provider
//    selects the implementation - "local" (default, Synology NAS mount) or
//    "s3" (AWSSDK.S3 - real Amazon S3, or any S3-compatible endpoint such as
//    the Floci emulator in the PR preview stack via BlobStorage:S3:ServiceUrl).
var blobStorageProvider = builder.Configuration["BlobStorage:Provider"] ?? "local";
switch (blobStorageProvider)
{
    case "s3":
        var s3ServiceUrl = builder.Configuration["BlobStorage:S3:ServiceUrl"];
        var s3BucketName = builder.Configuration["BlobStorage:S3:BucketName"] ?? "brewhouse-coffee-blobs";
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
// once-per-deploy step (`dotnet BrewHouse.dll db-apply`, run by
// deploy-blazor.yml before any node is deployed) - never on concurrent
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

// APP_OFFLINE.HTM: reproduces IIS/ASP.NET Core Module's app_offline.htm
// convention for this Kestrel/systemd host, which has no such thing built
// in. Checked first, ahead of every other middleware, so a dropped file
// takes the node offline for every real request. /health is deliberately
// EXEMPT (confirmed live against Kemp's actual config, docs/01 ADR 49): the
// VIP has no "Sorry Server" and no Not-Available-Redirection configured, so
// if /health also 503'd here, Kemp would mark both real servers Down with
// nothing to fall back to - visitors would hit Kemp's own generic error,
// never this page. Keeping /health honest lets Kemp keep routing normally,
// so real requests still reach a live Kestrel process serving the
// maintenance page - the whole point. Fixed path outside any
// releases/<sha> directory (not ContentRootPath) so it survives the
// symlink flip a deploy performs mid-maintenance (deploy-blazor.yml,
// patch.yml). www-data already has read/write there (blazor-app.service's
// ReadWritePaths).
const string appOfflinePath = "/var/www/brewhouse/app_offline.htm";
app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/health") && File.Exists(appOfflinePath))
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "text/html";
        await context.Response.SendFileAsync(appOfflinePath);
        return;
    }

    await next();
});

// Kemp (10.10.110.199) terminates TLS and forwards plain HTTP to :5000 for both its
// :80 and :443 VIPs, so Kestrel can't tell them apart from the connection alone -
// only X-Forwarded-Proto (set on the :443 VS's "Add Header to Request") does.
// KnownProxies is Kemp's own eth1 address (10.10.110.198, "Subnet Originating
// Requests") - the address Kemp actually connects from, not the VIP.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    KnownProxies = { System.Net.IPAddress.Parse("10.10.110.198") },
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseAntiforgery();
app.MapHealthChecks("/health"); // Kemp probes this endpoint
app.MapHub<BrewHouse.Hubs.BidsHub>("/hubs/bids");

// Serves a photo back out of whichever IBlobStore backend is configured (local
// NAS mount or S3) - the read half of the round-trip Home.razor's "Simulate
// Photo Upload" button writes through. GET-only and read-only, so no
// antiforgery/auth needed; LocalDiskBlobStore.Resolve() already guards against
// the {**path} route parameter escaping the configured storage root.
app.MapGet("/api/photos/{**path}", async (string path, IBlobStore blobStore) =>
{
    try
    {
        var (content, contentType) = await blobStore.ReadBytesAsync(path);
        return Results.File(content, contentType);
    }
    catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or AmazonS3Exception)
    {
        return Results.NotFound();
    }
});

app.MapRazorComponents<BrewHouse.Components.App>().AddInteractiveServerRenderMode();

// RunJasperFxCommands, not Run(): with no arguments this starts the web host exactly
// like app.Run(), but it also exposes JasperFx/Wolverine's CLI - notably
// `dotnet BrewHouse.dll db-apply`, which provisions Wolverine's envelope
// storage schema once per deploy (ADR 07). Without this the documented homelab
// step silently just booted the web app and the wolverine.* tables never existed.
return await app.RunJasperFxCommands(args);
