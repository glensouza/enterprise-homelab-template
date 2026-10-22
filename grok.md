# Grok change log

Local work from 2026-10-22. This file is the only commit on this branch. The code and video-script edits it describes are still uncommitted in the working trees, and nothing has been pushed to `main`.

Repos:

- `C:\github\glensouza\enterprise-homelab-template` (this file)
- `C:\github\glensouza\enterprise-homelab-template-video-scripts` (sibling)

Laptop start path stays `dotnet run --project src/BrewHouse.AppHost`. Docker Desktop is required for the AppHost and for the Aspire integration tests.

## What was verified

- Solution build succeeded (0 errors). Pre-existing package warnings remain: NU1902/NU1903 on MessagePack, System.Security.Cryptography.Pkcs, and System.Security.Cryptography.Xml, plus NU1510 and NU1608.
- Unit tests, filter `FullyQualifiedName!~Integration`: 50 passed.
- Aspire integration tests, filter `FullyQualifiedName~Integration`, Docker 29.9.1: 6 passed in 2m 49s. That includes writable Postgres, the rename migration keeping an existing row, nearest-lot cosine distance, S3 blob store, Wolverine durability, and dead-letter admin.
- The Blazor UI was not opened in a browser.

## Template app

### Health (ADR 129)

`/health` is still the Kemp probe and still returns the plain words `Healthy` or `Unhealthy`. `/health/detail` is new. `HealthCheckJsonWriter` writes `{ status, results }` with Postgres, Garnet, and RabbitMQ as separate entries. The offline-page middleware still lets every `/health` path through.

### Lot-list cache (ADR 130)

`ICoffeeLotReader` / `CoffeeLotReader` serve the coffee-lot list. `GarnetLotListCache` stores JSON at `brewhouse:coffee-lots` for five seconds. A miss loads `CoffeeLots` through `IDbContextFactory` (`AsNoTracking`, ordered by origin). `LiveBids` reads the list from the cache. `ProcessBidHandler` calls `InvalidateAsync` only after a bid is saved. The cache does not dial Garnet in its constructor, so `db-apply` and Wolverine codegen still boot when the cache is down.

### Redis traces (ADR 131)

OpenTelemetry registers `OpenTelemetry.Instrumentation.StackExchangeRedis` 1.17.0-beta.1 with parameterless `AddRedisInstrumentation()`. The `IConnectionMultiplexer` in DI is `LazyConnectionMultiplexer`, a `DispatchProxy`. It calls `ConnectionMultiplexer.Connect` on the first real command. SignalR, Data Protection, and the Redis health check keep their own connections.

### Conditional bids (ADR 132)

`CoffeeLot.CurrentBid` is an EF Core concurrency token. `BidRules.BeatsStandingBid` is `amount > standing`. `ProcessBidHandler` saves, and on `DbUpdateConcurrencyException` reloads once and tries again. A bid that no longer wins is logged and dropped. A second conflict follows Wolverine's retry and dead-letter path. `POST /api/v1/auctions/{id}/bids` uses the same rule against the cached standing bid and returns 409 before it publishes.

### pgvector nearest lot (ADR 133)

`CoffeeLots.Embedding` is a nullable `vector(32)`. Migration `20261022021222_AddCoffeeLotEmbedding` enables the `vector` extension and backfills the five seeded origins: Terraform Roast, Proxmox Pour-Over, Wolverine Dark Roast, Rabbit Hole Espresso, Garnet Reserve. `CoffeeLotEmbedding.Compute` hashes words and character trigrams into a 32-float unit vector. It does not call a hosted model. `SimilarLotQuery` runs only when the provider name contains Npgsql, excludes the anchor id, orders by cosine distance, and returns one other lot. `LiveBids` renders that lot in `<p class="similar-lot" data-testid="similar-lot">`. The InMemory provider ignores `Embedding` because `Pgvector.Vector` cannot be mapped there. Among the five seeds, Wolverine Dark Roast is the nearest neighbor of Terraform Roast.

The nearest-lot integration test deletes any coffee lot whose origin is not one of those five before it queries. The AppHost volume `brewhouse-postgres-data` persists across runs, and an extra row with the same embedding is cosine distance 0.

### Versioned API and YARP sample (ADR 134)

In-process routes on BrewHouse:

- `GET /api/v1/auctions`
- `GET /api/v1/auctions/{id}`
- `POST /api/v1/auctions/{id}/bids` publishes `ProcessBidMessage` and returns 202, 409, or 404
- `GET /api/v2/auctions` returns `{ id, origin, bid }`

`Asp.Versioning.Http` 10.2.3 reads the version from the URL segment. Unversioned `/api/auctions` is 404. `Asp.Versioning.Mvc.ApiExplorer` 10.2.1 and `Asp.Versioning.OpenApi` 10.2.3 serve `/openapi/v1.json` and `/openapi/v2.json` through `MapOpenApi().WithDocumentPerVersion()`. Scalar.AspNetCore 2.18.2 lists both. There is no committed `openapi.json`. `Microsoft.AspNetCore.OpenApi` is 10.0.13.

`samples/YarpGateway` (Yarp.ReverseProxy 2.3.0) listens on `http://localhost:5080`, returns `yarp-gateway-sample` from `GET /`, and proxies `/api/{**catch-all}` to `localhost:5000` and `localhost:5001`. It is in the solution under `/samples/`. Ansible, Terraform, and the workflows do not install it.

Gravitee, a second gateway, API keys, and a developer portal are not in this template.

### UI boundary

The Blazor UI is still the lot list, the bid button, the photo round-trip, and `/admin/dead-letters`. The new surface on the lot list is the one nearest-lot line.

### Docs and readability

- README opens with two sentences: the web tier is two nodes behind Kemp; Postgres, Garnet, and RabbitMQ are single instances on pve4; recovery is pgBackRest and `rollback.yml`.
- 44 incident ADR write-ups moved from `docs/01-architecture-decisions.md` to `docs/incidents.md`. Each number keeps its `## ADR NN:` heading in `docs/01`, plus one line pointing at `docs/incidents.md#adr-NN`. ADR 53 stays in full in `docs/01`. ADRs 129–134 are new and sit in `docs/01` before the attribution footer.
- `CLAUDE.md`, `docs/06-testing-strategy.md`, `docs/07-observability.md`, and `docs/12-request-flows.md` match the new health, cache, trace, bid, vector, and API behavior.
- Package versions live in `Directory.Packages.props`.

### Template files touched

Modified:

- `CLAUDE.md`, `README.md`, `Directory.Packages.props`, `enterprise-homelab-template.slnx`
- `docs/01-architecture-decisions.md`, `docs/06-testing-strategy.md`, `docs/07-observability.md`, `docs/12-request-flows.md`
- `src/BrewHouse/BrewHouse.csproj`, `Program.cs`, `Components/LiveBids.razor`, `wwwroot/css/app.css`
- `src/BrewHouse/Data/AuctionDbContext.cs`, `Data/AuctionDbContextFactory.cs`, `Migrations/AuctionDbContextModelSnapshot.cs`
- `src/BrewHouse/Services/ProcessBidHandler.cs`
- `tests/BrewHouse.Tests/BrewHouse.Tests.csproj`, `DatabaseIntegrationTests.cs`, `HomeComponentTests.cs`, `LiveBidsComponentTests.cs`, `ProcessBidHandlerTests.cs`

Added:

- `docs/incidents.md`
- `samples/YarpGateway/` (`Program.cs`, `YarpGateway.csproj`, `appsettings.json`, `README.md`)
- `src/BrewHouse/Data/CoffeeLotEmbedding.cs`, `Data/CoffeeLotSnapshot.cs`
- `src/BrewHouse/Migrations/20261022021222_AddCoffeeLotEmbedding.cs` and its Designer
- `src/BrewHouse/Services/AuctionApi.cs`, `BidRules.cs`, `CoffeeLotReader.cs`, `GarnetLotListCache.cs`, `HealthCheckJsonWriter.cs`, `ICoffeeLotReader.cs`, `LazyConnectionMultiplexer.cs`, `SimilarLotQuery.cs`
- `tests/BrewHouse.Tests/AuctionApiTests.cs`, `CoffeeLotEmbeddingTests.cs`, `CoffeeLotReaderTests.cs`, `HealthCheckJsonWriterTests.cs`

`tests/BrewHouse.Tests` is 15 `*Tests.cs` files.

## Video scripts

Sibling repo only. Statuses are `shoot` or `cut`. The "shoot after demo fixes" bucket is empty.

Paths in the scripts now point at `src/BrewHouse`, `tests/BrewHouse.Tests`, `brewhouse.internal`, `/var/www/brewhouse/`, and `/etc/brewhouse/brewhouse.env`.

Fleet copy uses 16 LXCs. VLAN 130 is the management network. `pve1`–`pve4` stay on the existing `10.10.10.0/24` LAN. CritterWatch is in the full tour. "Twelve" in S1-05 is admin panels. "twelve minutes" in S1-09 is unchanged.

`SHOOT-LIST.md` is the filming checklist. `DEMO-AUDIT.md` and `CONSOLIDATION-TASKS.md` have a historical banner and were not rewritten; their bodies still say RoadrunnerAuction and 12 LXCs on purpose.

Publish order: S1-01 through S1-12, skip 13, S1-14, skip 15–16, S1-17, skip 18, S1-19, skip 20, S1-21, skip 22, S1-23, then S2-01 through S2-09. `SCHEDULE.md` fortnight 14 is "16 Proxmox LXCs". Fortnights 13, 15, 16, 18, 20, and 22 say they are cut and not to record. Script files stay.

- S1-01 includes a ~30 second beat that opens `/admin/dead-letters`.
- S1-08 is the in-process versioned minimal API. Demo `/api/v1` and `/api/v2`. OpenAPI is the runtime documents plus Scalar.
- S1-09 ends the API arc. Shoot on a laptop with `dotnet run --project samples/YarpGateway`, show `http://localhost:5080`, then stop the process. The sample does not get a VLAN. Gravitee is a survey entry and is not installed.
- `episodes/09` stays a Short. The nearest-lot query belongs to S2-07.

Modified in the sibling repo: `SERIES.md`, `README.md`, `SCHEDULE.md`, `DEMO-AUDIT.md`, `CONSOLIDATION-TASKS.md`, `episodes/01`, `06`, `11`, `14`, `15`, `S1-06`, `S1-08`, `S1-09`, `S1-13`, `S1-14`, `S1-15`, `S1-16`, `S1-18`, `S1-20`, `S1-22`, `S1-23`, and the matching `social/` files for 06, 11, 14, 15, S1-06, and S1-14.

Added: `SHOOT-LIST.md`.

## Left alone

No second app name. No Gravitee, second gateway, API keys, developer portal, or Postgres replication. No new fortnights. No Terraform, Ansible, or workflow changes for the YARP sample. No `DROP DATABASE`, no `pct destroy`, no NAS wipe. The code stays uncommitted. This branch contains `grok.md` only.
