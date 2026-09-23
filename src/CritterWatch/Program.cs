// Confirmed live via ilspycmd decompile of the real 1.0.1 package (the
// upstream quickstart docs page, fetched through an AI-summarizing web
// tool, didn't name the actual namespace) - AddCritterWatch()/
// UseCritterWatch() live in CritterWatch.Services.Hosting, not a bare
// top-level "CritterWatch" namespace.
using CritterWatch.Services.Hosting;
using Wolverine.RabbitMQ;

var builder = WebApplication.CreateBuilder(args);

// Fail fast in Production when secrets were not injected - same discipline
// as BrewHouse's own RequireConnectionString (src/BrewHouse/Program.cs).
// Production: the Infisical Agent renders /etc/critterwatch/critterwatch.env,
// loaded by systemd via EnvironmentFile= (src/systemd/critterwatch.service).
string RequireConnectionString(string name)
{
    var value = builder.Configuration.GetConnectionString(name);
    if (string.IsNullOrWhiteSpace(value))
        throw new InvalidOperationException(
            $"Connection string '{name}' is missing. Ensure /etc/critterwatch/critterwatch.env " +
            "(rendered by the Infisical Agent) defines ConnectionStrings__" + name + ".");
    return value;
}

var dbConnectionString = RequireConnectionString("critterwatch");
var rabbitConnectionString = RequireConnectionString("messaging");

// docs/01 ADR 91 - the console side of CritterWatch (JasperFx.net/critterwatch).
// Monitors BrewHouse over the same RabbitMQ transport BrewHouse itself uses
// (MessagingTransportConfigurator.cs), not a direct DB connection or an
// embedded agent - reachable at "critterwatch" / control at "brewhouse-control",
// matching the queue names BrewHouse's own AddCritterWatchMonitoring() call
// points at.
builder.AddCritterWatch(
    dbConnectionString,
    opts =>
    {
        // Same minimal shape BrewHouse's own MessagingTransportConfigurator
        // already proves works (no chained .ProcessInParallelWithNativeAcks()/
        // .UseCritterWatchSerializer() - neither exists on this real 1.0.1
        // package per ilspycmd; AddCritterWatch()'s own internal
        // AddCritterWatchServices() call already wires the serializer policy
        // globally).
        opts.UseRabbitMq(new Uri(rabbitConnectionString))
            .AutoProvision();
        opts.ListenToRabbitQueue("critterwatch");
    });

var app = builder.Build();
app.UseCritterWatch();
app.Run();
