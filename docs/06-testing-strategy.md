# Testing Strategy

This architecture demands a multi-tiered testing strategy that balances execution speed with rigorous dependency verification. Component and handler tests run fully in-memory; database behavior is verified against a real PostgreSQL container orchestrated by the same Aspire graph used for local development.

---

## 1. Blazor UI Testing (bUnit + xUnit + Moq)
We use `bUnit` 2.7.2 (stable) integrated with `xUnit` to test Razor components in isolation without spinning up a real headless browser. Test classes derive from `BunitContext`.
* **Dependency Injection:** We use `Moq` to inject fake versions of `IBlobStore`, Wolverine's `IMessageBus`, and `IBidsClient`. `LiveBids` is tested through `ICoffeeLotReader` and `ISimilarLotQuery`. `Home` injects `IDbContextFactory<AuctionDbContext>`; the photo button itself writes through `IBlobStore`. Components take a factory or a reader, not a scoped context, because Blazor Server circuits are long-lived.
* **State Verification:** bUnit renders the DOM tree entirely in-memory, allowing assertions against specific HTML updates or parameter changes when buttons are clicked.

## 2. Messaging Handler Testing (Wolverine Unit Tests)
Wolverine handlers are plain static classes, so we test them by invoking the `Handle` method directly — no broker, test harness, or container required.
* **Direct Invocation:** Call `ProcessBidHandler.Handle(message, dbContext, hubContext, lots, logger)` and assert completion or expected exceptions (e.g., unknown coffee lot throws `InvalidOperationException`). `lots` is an `ICoffeeLotReader`; the handler invalidates it only after a saved bid.
* **Concurrency:** `CurrentBid` is a concurrency token (ADR 132). `Handle_LosingConcurrentBid_Does_Not_Overwrite_The_Winner` loads the same row in two in-memory contexts and checks that the lower bid does not replace the winner.
* **Scope:** An EF Core in-memory context is acceptable here because these tests verify handler logic only, never relational/database behavior. The InMemory provider ignores `CoffeeLot.Embedding` (`vector(32)`); nearest-lot behavior is an integration test.

## 3. Database Integration Testing (Aspire.Hosting.Testing)
Relational behavior is verified against the real database, not a fake.
* **Real Provider:** `Aspire.Hosting.Testing` boots the same AppHost used for local development and returns a live connection string to a real PostgreSQL (`pgvector/pgvector:pg16`) container.
* **Docker Required:** These tests need a running Docker daemon; they execute in PR CI on `ubuntu-latest`, where Docker is available.
* **EF Core InMemory is NOT used for verifying relational behavior anymore.** The InMemory provider does not enforce relational semantics, constraints, transactions, or provider-specific behavior (e.g., Npgsql/pgvector mappings), so green tests against it can hide failures that only appear against real PostgreSQL.
* **pgvector:** `Nearest_Lot_Uses_Cosine_Distance_On_Postgres` migrates the real database, inserts three lots with embeddings from `CoffeeLotEmbedding.Compute`, and asserts the cosine neighbor (ADR 133). The rename-migration test reads `CurrentBid` with SQL because it stops at the migration from before the embedding column existed.

## 4. API, cache, and health

These stay in the unit suite. They do not boot Aspire.

* **`AuctionApiTests`:** TestServer calls `/api/v1/auctions`, `/api/v2/auctions`, and `POST /api/v1/auctions/{id}/bids`, and reads `/openapi/v1.json` and `/openapi/v2.json` (ADR 134).
* **`CoffeeLotReaderTests`:** the lot list is loaded once and a successful invalidation forces the next read back to the database (ADR 130).
* **`CoffeeLotEmbeddingTests`:** the local feature hash, with no database. "Terraform Roast" is nearer "Wolverine Dark Roast" than "Garnet Reserve".
* **`HealthCheckJsonWriterTests`:** `/health/detail` JSON names each dependency. `/health` itself stays the one-word Kemp probe and is not this writer (ADR 129).

## 5. Pre-Merge Environments (PR Previews)
Automated tests verify units and provider behavior; they do not verify the fully assembled system (migrations + messaging + cache + UI over real HTTPS). Every open PR therefore gets an ephemeral, fully-integrated preview environment on the non-prod preview host (VLAN 140) — the exact compose equivalent of the homelab dependency graph — deployed by `pr-preview.yml` after the test suite passes, and torn down on merge/close by `pr-preview-cleanup.yml` (ADR 19/20).
* **Scope:** manual exploratory testing, stakeholder review, and migration smoke-testing against real PostgreSQL/Garnet/RabbitMQ before merge. The workflow itself smoke-tests `/health` over the trusted internal CA chain.
* **Isolation guarantee:** VLAN 140 is firewalled from all homelab tiers; preview data is disposable and destroyed with the environment. Full guide: `docs/11-pr-preview-environments.md`.

---
### Source Material & Attribution
bUnit testing guidelines, Wolverine handler documentation, and Microsoft Aspire testing documentation (`Aspire.Hosting.Testing`).
