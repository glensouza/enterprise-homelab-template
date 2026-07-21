# Architectural Decision Records (ADRs)

---

## ADR 01: Decentralized LXC Model vs. Monolithic Docker Host
* **Decision:** Deploy services as native, individual Linux Containers (LXCs) on Proxmox.
* **Rationale:** Fault Isolation, Resource Overhead reduction, and native backup granularity.

---

## ADR 02: Bare-Metal .NET 10 & Systemd vs. Docker Containerization
* **Decision:** Host .NET 10 Blazor applications natively on Debian LXCs running as `systemd` daemons.
* **Rationale:** Eliminates Docker engine overhead and simplifies CI/CD.

---

## ADR 03: Poly-Cloud Storage Abstraction via FluentStorage
* **Decision:** Use the `FluentStorage` NuGet package inside C# Blazor apps.
* **Rationale:** Vendor Lock-In Prevention and flexibility to swap between local Synology disks and cloud blobs.

---

## ADR 04: Microsoft Garnet for In-Memory Caching
* **Decision:** Deploy Microsoft Garnet instead of standard Redis.
* **Rationale:** Multi-threaded .NET backend, high performance, and RESP protocol compatibility.

---

## ADR 05: Layer 7 High Availability via Kemp LoadMaster
* **Decision:** Deploy dual Blazor Web LXCs across two separate Proxmox nodes behind a Kemp LoadMaster Virtual Service VIP (`10.10.10.199`).
* **Rationale:** Zero-Downtime Maintenance and active health checking.

---

## ADR 06: UniFi Network Segregation (VLANs)
* **Decision:** Segregate the architecture into three isolated VLANs (10, 20, 30) managed by the UniFi gateway.
* **Rationale:** Prevents unauthorized lateral movement. 

---

## ADR 07: Message Brokering Abstraction via MassTransit
* **Decision:** Use MassTransit for all asynchronous messaging and background processing, backed by RabbitMQ in the home lab.
* **Rationale:** Allows application code to rely on generic `IBus` and `IConsumer` interfaces. If the application migrates to Azure or AWS, the RabbitMQ transport can be swapped to Azure Service Bus or Amazon SQS purely via configuration in `Program.cs`.

---

## ADR 08: SignalR WebSockets Scale-Out via Microsoft Garnet Backplane & Kemp Sticky Sessions
* **Decision:** Configure Blazor Server SignalR hubs to use Microsoft Garnet as a distributed pub/sub backplane (`AddStackExchangeRedis`), while enforcing L7 Session Persistence (Sticky Sessions) on the Kemp LoadMaster.
* **Rationale:** Blazor Server holds circuit state in application RAM. Sticky Sessions ensure a user's persistent WebSocket connection remains anchored to their assigned Blazor LXC node. If a cross-node broadcast occurs (e.g., live auction bid update), Garnet synchronizes the SignalR hubs across `Web 01` and `Web 02` instantaneously.

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

---
## ADR 13: Let's Encrypt Wildcard Certificates via DNS-01 & Kemp LoadMaster
* **Decision:** Utilize the native Let's Encrypt ACMEv2 client built into the Kemp LoadMaster to automatically request and renew wildcard certificates (`*.yourdomain.com`) using the DNS-01 challenge against the Cloudflare API.
* **Rationale:** The DNS-01 challenge allows for the issuance of trusted certificates for internal, non-publicly routable networks without opening firewall ports. By terminating SSL at the Kemp LoadMaster, all Blazor backend LXCs are offloaded from certificate management, avoiding "Not Secure" browser warnings for internal admin endpoints.
