# Testing Strategy

This architecture demands a multi-tiered testing strategy that balances execution speed with rigorous dependency verification. By leveraging the appropriate in-memory abstractions, the entire test suite can be run completely disconnected from the local Proxmox environment or Docker services.

---

## 1. Blazor UI Testing (bUnit + xUnit)
We use `bUnit` integrated with `xUnit` to test Razor components in isolation without spinning up a real headless browser. 
* **Dependency Injection:** We use `Moq` to inject fake versions of `IBlobStorage`, `IConnectionMultiplexer` (Garnet), and the MassTransit `IBus`.
* **State Verification:** bUnit renders the DOM tree entirely in-memory, allowing assertions against specific HTML updates or parameter changes when buttons are clicked.

## 2. Message Bus Testing (MassTransit TestHarness)
Testing queues typically requires spinning up RabbitMQ containers in a CI pipeline. We bypass this entirely.
* **In-Memory Harness:** MassTransit provides a built-in `ITestHarness` that routes messages completely in-memory.
* **Consumer Verification:** Verifies that a message was published and processed by `BidProcessingConsumer` within milliseconds.

## 3. Database Testing (EF Core In-Memory)
We swap the PostgreSQL provider for `UseInMemoryDatabase()` during test initialization. This guarantees that Entity Framework queries execute realistically without needing a live database.
