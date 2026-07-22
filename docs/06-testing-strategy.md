# Testing Strategy

This architecture demands a multi-tiered testing strategy that balances execution speed with rigorous dependency verification. Component and handler tests run fully in-memory; database behavior is verified against a real PostgreSQL container orchestrated by the same Aspire graph used for local development.

---

## 1. Blazor UI Testing (bUnit + xUnit + Moq)
We use `bUnit` 2.7.2 (stable) integrated with `xUnit` to test Razor components in isolation without spinning up a real headless browser. Test classes derive from `BunitContext`.
* **Dependency Injection:** We use `Moq` to inject fake versions of `IBlobStore`, Wolverine's `IMessageBus`, and `IDbContextFactory<AuctionDbContext>` (components use the factory, not a scoped context, because Blazor Server circuits are long-lived).
* **State Verification:** bUnit renders the DOM tree entirely in-memory, allowing assertions against specific HTML updates or parameter changes when buttons are clicked.

## 2. Messaging Handler Testing (Wolverine Unit Tests)
Wolverine handlers are plain static classes, so we test them by invoking the `Handle` method directly — no broker, test harness, or container required.
* **Direct Invocation:** Call `ProcessBidHandler.Handle(message, dbContext, logger)` and assert completion or expected exceptions (e.g., unknown equipment throws `InvalidOperationException`).
* **Scope:** An EF Core in-memory context is acceptable here because these tests verify handler logic only, never relational/database behavior.

## 3. Database Integration Testing (Aspire.Hosting.Testing)
Relational behavior is verified against the real database, not a fake.
* **Real Provider:** `Aspire.Hosting.Testing` boots the same AppHost used for local development and returns a live connection string to a real PostgreSQL (`pgvector/pgvector:pg16`) container.
* **Docker Required:** These tests need a running Docker daemon; they execute in PR CI on `ubuntu-latest`, where Docker is available.
* **EF Core InMemory is NOT used for verifying relational behavior anymore.** The InMemory provider does not enforce relational semantics, constraints, transactions, or provider-specific behavior (e.g., Npgsql/pgvector mappings), so green tests against it can hide failures that only appear against real PostgreSQL.

## 4. Pre-Merge Environments (PR Previews)
Automated tests verify units and provider behavior; they do not verify the fully assembled system (migrations + messaging + cache + UI over real HTTPS). Every open PR therefore gets an ephemeral, fully-integrated preview environment on the non-prod preview host (VLAN 40) — the exact compose equivalent of the production dependency graph — deployed by `pr-preview.yml` after the test suite passes, and torn down on merge/close by `pr-preview-cleanup.yml` (ADR 19/20).
* **Scope:** manual exploratory testing, stakeholder review, and migration smoke-testing against real PostgreSQL/Garnet/RabbitMQ before merge. The workflow itself smoke-tests `/health` over the trusted internal CA chain.
* **Isolation guarantee:** VLAN 40 is firewalled from all production tiers; preview data is disposable and destroyed with the environment. Full guide: `docs/11-pr-preview-environments.md`.

---
### Source Material & Attribution
bUnit testing guidelines, Wolverine handler documentation, and Microsoft Aspire testing documentation (`Aspire.Hosting.Testing`).
