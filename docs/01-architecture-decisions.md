# Architectural Decision Records (ADRs)

---
## ADR 01-08: Found in previous revisions.

---
## ADR 09: Structured Logging with Grafana Loki
* **Decision:** Centralize application and system logs using Grafana Loki on VLAN 30, pushing logs directly from .NET using Serilog.
* **Rationale:** Blazor Server apps spread across multiple LXC nodes (`Web 01`, `Web 02`) make SSH/`journalctl` debugging unscalable. Loki provides a cloud-agnostic, low-overhead centralized tracing mechanism.

---
## ADR 10: Deep Health Checks for Kemp L7
* **Decision:** Expose `/health` using `Microsoft.Extensions.Diagnostics.HealthChecks` to verify Postgres, Garnet, and RabbitMQ connectivity.
* **Rationale:** Prevents Kemp from routing traffic to a Blazor LXC that has lost backend connectivity, enabling true zero-downtime failover.

---
## ADR 11: EF Core Migration Bundles in CI/CD
* **Decision:** Generate and execute standalone EF Core Migration Bundles during the GitHub Actions deployment pipeline, instead of running `context.Database.Migrate()` on application boot.
* **Rationale:** Prevents database lock contention and schema race conditions when both HA nodes start simultaneously.

---
## ADR 12: Secrets Management via Infisical SDK
* **Decision:** Inject Infisical SDK into the application bootstrap to resolve credentials dynamically.
* **Rationale:** Eliminates plain-text passwords in `appsettings.json` and secures them within the isolated VLAN 30 Infisical vault.

---
### Source Material & Attribution
Decisions regarding Serilog/Loki derive from Grafana Labs guidelines. EF Bundle architecture follows Microsoft's Production Deployment guidelines for Entity Framework Core.
