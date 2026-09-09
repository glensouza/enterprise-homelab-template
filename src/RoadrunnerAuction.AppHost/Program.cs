var builder = DistributedApplication.CreateBuilder(args);

// Stable local-dev credentials for the resources that keep a data volume.
// Aspire generates a fresh random password on every run, but WithDataVolume keeps
// the cluster that the FIRST run initialized - so a generated password authenticates
// exactly once and every later run dies with 28P01 / BrokerUnreachableException until
// the volume is deleted. Pinning them keeps the volumes reusable across runs.
// Local dev only: production credentials come from Infisical (ADR 12) and never
// from this file.
var postgresPassword = builder.AddParameter("postgres-password", "roadrunner-local-dev", secret: true);
var messagingPassword = builder.AddParameter("messaging-password", "roadrunner-local-dev", secret: true);

// PostgreSQL with pgvector (matches VLAN 20 production image)
var postgres = builder.AddPostgres("postgres", password: postgresPassword)
    .WithImage("pgvector/pgvector")
    .WithImageTag("pg16")
    .WithDataVolume("roadrunner-postgres-data");
var roadrunnerDb = postgres.AddDatabase("roadrunnerdb");

// Microsoft Garnet (RESP-compatible, matches VLAN 20 production cache).
// Tag pinned deliberately - "latest" makes local runs and CI drift apart silently.
var cache = builder.AddRedis("cache")
    .WithImage("ghcr.io/microsoft/garnet")
    .WithImageTag("2.1.7")
    .WithDataVolume("roadrunner-cache-data");

// RabbitMQ with management UI (matches VLAN 20 production broker)
var messaging = builder.AddRabbitMQ("messaging", password: messagingPassword)
    .WithManagementPlugin()
    .WithDataVolume("roadrunner-rabbitmq-data");

// Blazor app - connection strings are injected dynamically by Aspire
// as ConnectionStrings__roadrunnerdb / ConnectionStrings__cache / ConnectionStrings__messaging
builder.AddProject<Projects.RoadrunnerAuction>("roadrunnerauction")
    .WithReference(roadrunnerDb)
    .WithReference(cache)
    .WithReference(messaging)
    .WaitFor(roadrunnerDb)
    .WaitFor(cache)
    .WaitFor(messaging);

builder.Build().Run();
