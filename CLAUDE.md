# Claude Code Instructions & Project Context

You are operating within an Enterprise Proxmox Home Lab template. Your primary goal is to assist the developer in writing .NET 10 Blazor code, managing LXC infrastructure, maintaining CI/CD pipelines, and keeping documentation and tests strictly aligned with code changes.

---

## 1. MANDATORY AI DEVELOPER WORKFLOW (ALWAYS ENFORCE)

Whenever you touch code, refactor logic, modify infrastructure configs, or add features, **you MUST automatically consider and execute the following two steps before completing your task:**

1. **Synchronize Tests (`tests/RoadrunnerAuction.Tests/`):**
   * **Component/UI Changes:** Update corresponding `bUnit` tests.
   * **Messaging/Consumer Changes:** Update `MassTransit TestHarness` tests.
   * **Data Model / EF Core Changes:** Add/update model tests using EF Core's In-Memory provider.
   * **Verification:** Ensure all tests build and pass (`dotnet test`).

2. **Synchronize Documentation (`docs/` & `CLAUDE.md`):**
   * **Architecture Changes:** Create/update an ADR in `docs/01-architecture-decisions.md`.
   * **Infrastructure Changes:** Update the matrix in `CLAUDE.md` and `docs/05-unifi-network-isolation.md`.

---

## 2. Project Context & Network Topology (Memory)

### VLAN 10 (Web / Ingress Tier) - `10.10.10.x`
* **Synology NAS IP:** `10.10.10.90`
* **Kemp LoadMaster VIP:** `10.10.10.199` (Sticky Sessions enabled)
* **Cloudflared Tunnel LXC:** `10.10.10.5`
* **Blazor Web 01 (Node 1):** `10.10.10.101`
* **Blazor Web 02 (Node 2):** `10.10.10.102`

### VLAN 20 (Backend / Data Tier) - `10.10.20.x`
* **PostgreSQL (pgvector):** `10.10.20.110` (Node 1)
* **Microsoft Garnet Cache:** `10.10.20.111` (Node 1)
* **RabbitMQ:** `10.10.20.112` (Node 1)

### VLAN 30 (Management / Infrastructure Tier) - `10.10.30.x`
* **Proxmox Node 1:** `10.10.30.10`
* **Proxmox Node 2:** `10.10.30.11`
* **Zot Registry:** `10.10.30.115` (Node 2)
* **Infisical:** `10.10.30.116` (Node 2)
* **Uptime Kuma:** `10.10.30.117` (Node 2)
* **Grafana Loki / Observability:** `10.10.30.118` (Node 2)

---

## 3. Core Architectural Rules (Skills & Guardrails)

### A. .NET 10 Blazor & Systemd
- Apps are deployed to native Debian LXCs and managed strictly via `systemd`.
- **Health Checks:** Always maintain `/health` endpoint mapping deep checks for Kemp L7 routing.
- **Secrets:** Avoid plain-text passwords in `appsettings.json`. Use `Infisical.Sdk`.
- **Logging:** Use `Serilog` with the Grafana Loki sink.

### B. CI/CD & Deployments
- **Migrations:** GitHub Actions MUST generate and execute EF Core Migration Bundles BEFORE deploying web apps to prevent race conditions.

### C. Safety Guardrails (RED RULES)
- **LXC Deletion:** Never run `pct destroy <vmid>`. 
- **Data Wipe:** Never run `rm -rf` on any directory mapped to a Synology NAS.
- **Database Drops:** Never execute `DROP DATABASE` or `DROP TABLE` without explicit confirmation.
