var builder = DistributedApplication.CreateBuilder(args);

// PostgreSQL with pgvector (matches VLAN 20 production image)
var postgres = builder.AddPostgres("postgres")
    .WithImage("pgvector/pgvector")
    .WithImageTag("pg16")
    .WithDataVolume("roadrunner-postgres-data");
var roadrunnerDb = postgres.AddDatabase("roadrunnerdb");

// Microsoft Garnet (RESP-compatible, matches VLAN 20 production cache)
var cache = builder.AddRedis("cache")
    .WithImage("ghcr.io/microsoft/garnet")
    .WithImageTag("latest")
    .WithDataVolume("roadrunner-cache-data");

// RabbitMQ with management UI (matches VLAN 20 production broker)
var messaging = builder.AddRabbitMQ("messaging")
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
