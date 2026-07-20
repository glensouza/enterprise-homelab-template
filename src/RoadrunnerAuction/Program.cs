using FluentStorage;
using FluentStorage.Blobs;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using Serilog;
using Serilog.Sinks.Grafana.Loki;
using Infisical.Sdk;
using RoadrunnerAuction.Data;
using RoadrunnerAuction.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. OBSERVABILITY: Serilog + Grafana Loki
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.GrafanaLoki(builder.Configuration["GrafanaLoki:Endpoint"]!)
    .CreateLogger();
builder.Host.UseSerilog();

// 2. SECRETS: Infisical Resolution (Simulated logic for production)
string dbConnectionString = builder.Configuration.GetConnectionString("PostgresConnection") ?? "";
string redisConnectionString = builder.Configuration.GetConnectionString("GarnetCache") ?? "";
string rabbitConnectionString = builder.Configuration.GetConnectionString("RabbitMqHost") ?? "";

if (!builder.Environment.IsDevelopment())
{
    /* 
     * In Production, replace the empty strings above by fetching from Infisical vault.
     * Example Implementation:
     * 
     * var infisical = new InfisicalClient(new ClientSettings { SiteUrl = builder.Configuration["Infisical:SiteUrl"] });
     * // Auth logic here...
     * dbConnectionString = (await infisical.GetSecretAsync(new GetSecretOptions { SecretName = "DB_CONN", ... })).SecretValue;
     */
}

// 3. DATABASE
builder.Services.AddDbContext<AuctionDbContext>(options =>
    options.UseNpgsql(dbConnectionString));

// 4. CACHE & SIGNALR BACKPLANE
builder.Services.AddSingleton<IConnectionMultiplexer>(sp => 
    ConnectionMultiplexer.Connect(redisConnectionString));
builder.Services.AddSignalR().AddStackExchangeRedis(redisConnectionString);

// 5. STORAGE
builder.Services.AddSingleton<IBlobStorage>(sp => 
    StorageFactory.Blobs.FromConnectionString(builder.Configuration.GetConnectionString("FluentStorage")));

// 6. QUEUE
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<BidProcessingConsumer>();
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(new Uri(rabbitConnectionString));
        cfg.ConfigureEndpoints(context);
    });
});

// 7. DEEP HEALTH CHECKS (For Kemp L7)
builder.Services.AddHealthChecks()
    .AddNpgSql(dbConnectionString)
    .AddRedis(redisConnectionString)
    .AddRabbitMQ(rabbitConnectionString: rabbitConnectionString);

// 8. BLAZOR WEBSOCKET TIMEOUTS
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddServerSideBlazor(options => {
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(3);
})
.AddHubOptions(options => {
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.HandshakeTimeout = TimeSpan.FromSeconds(30);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment()) {
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseAntiforgery();
app.MapHealthChecks("/health"); // Kemp probes this endpoint
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
