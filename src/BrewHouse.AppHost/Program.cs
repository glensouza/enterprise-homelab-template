var builder = DistributedApplication.CreateBuilder(args);

// Stable local-dev credentials for the resources that keep a data volume.
// Aspire generates a fresh random password on every run, but WithDataVolume keeps
// the cluster that the FIRST run initialized - so a generated password authenticates
// exactly once and every later run dies with 28P01 / BrokerUnreachableException until
// the volume is deleted. Pinning them keeps the volumes reusable across runs.
// Local dev only: production credentials come from Infisical (ADR 12) and never
// from this file.
var postgresPassword = builder.AddParameter("postgres-password", "brewhouse-local-dev", secret: true);
var messagingPassword = builder.AddParameter("messaging-password", "brewhouse-local-dev", secret: true);

// PostgreSQL with pgvector (matches VLAN 120 production image)
var postgres = builder.AddPostgres("postgres", password: postgresPassword)
    .WithImage("pgvector/pgvector")
    .WithImageTag("pg16")
    .WithDataVolume("brewhouse-postgres-data");
var brewhouseDb = postgres.AddDatabase("brewhousedb");

// Microsoft Garnet (RESP-compatible, matches VLAN 120 production cache).
// Tag pinned deliberately - "latest" makes local runs and CI drift apart silently.
//
// Two confirmed-live gotchas from wiring this up, both required:
// 1. Image reference is "microsoft/garnet", NOT "ghcr.io/microsoft/garnet" -
//    Aspire's DCP fails to pull the ghcr.io-qualified reference directly
//    ("pull access denied ... repository does not exist"), even though a
//    plain `docker pull` of that exact reference succeeds and the image
//    ends up in `docker images` just fine. A DCP/GHCR-specific
//    incompatibility, not a real permissions issue - CI pre-pulls from
//    ghcr.io and re-tags to this registry-less local name before tests run
//    (see deploy-blazor.yml/pr-preview.yml); do the same for local dev:
//      docker pull ghcr.io/microsoft/garnet:2.1.7
//      docker tag ghcr.io/microsoft/garnet:2.1.7 microsoft/garnet:2.1.7
// 2. WithEntrypoint is required - AddRedis("cache") still injects Aspire's
//    own `redis-server` startup command regardless of the swapped image,
//    and Garnet's image has no such binary (only /app/GarnetServer),
//    crashing with exit code 127 ("redis-server: not found") without this.
var cache = builder.AddRedis("cache")
    .WithImage("microsoft/garnet")
    .WithImageTag("2.1.7")
    .WithEntrypoint("/app/GarnetServer")
    .WithDataVolume("brewhouse-cache-data");

// RabbitMQ with management UI (matches VLAN 120 production broker)
var messaging = builder.AddRabbitMQ("messaging", password: messagingPassword)
    .WithManagementPlugin()
    .WithDataVolume("brewhouse-rabbitmq-data");

// Blazor app - connection strings are injected dynamically by Aspire
// as ConnectionStrings__brewhousedb / ConnectionStrings__cache / ConnectionStrings__messaging
builder.AddProject<Projects.BrewHouse>("brewhouseauction")
    .WithReference(brewhouseDb)
    .WithReference(cache)
    .WithReference(messaging)
    .WaitFor(brewhouseDb)
    .WaitFor(cache)
    .WaitFor(messaging);

builder.Build().Run();
