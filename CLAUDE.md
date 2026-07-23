# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Restore dependencies
dotnet restore

# Build solution
dotnet build

# Run the app locally (start here for local development)
dotnet run --project src/RoadrunnerAuction.AppHost

# Run tests
dotnet test
```

Docker Desktop must be running before starting the AppHost — it launches PostgreSQL (pgvector), Garnet, and RabbitMQ containers and injects their connection strings into the app as `ConnectionStrings__roadrunnerdb` / `__cache` / `__messaging`. Integration tests reuse the exact same dependency graph via `Aspire.Hosting.Testing`.

## Mandatory AI developer workflow (always enforce)

Whenever you touch code, refactor logic, modify infrastructure configs, or add features, you MUST execute both of these before completing your task:

1. **Synchronize tests (`tests/RoadrunnerAuction.Tests/`):**
   - **Component/UI changes:** update corresponding `bUnit` tests.
   - **Messaging/handler changes:** update `Wolverine` handler unit tests (plain method invocation).
   - **Data model / EF Core changes:** add/update `Aspire.Hosting.Testing` integration tests against the real pgvector PostgreSQL container (Docker required) for provider-real behavior.
   - **Verification:** ensure all tests build and pass (`dotnet test`).

2. **Synchronize documentation (`docs/` & `CLAUDE.md`):**
   - **Architecture changes:** create/update an ADR in `docs/01-architecture-decisions.md`.
   - **Infrastructure changes:** update the topology matrix below and `docs/05-unifi-network-isolation.md`; keep `terraform/lxc.tf` / `terraform/unifi.tf` in sync with `docs/04` / `docs/05`.

## Architecture

This is a production-grade template for hosting **.NET 10 Blazor Server** apps on **Proxmox VE** as decentralized native LXCs (no Docker in production — ADR 01/02). The solution:

| Path | Role |
|------|------|
| `src/RoadrunnerAuction` | The Blazor Server app — Wolverine messaging, EF Core + pgvector, SignalR scale-out via Garnet backplane, `/health` deep checks |
| `src/RoadrunnerAuction.AppHost` | .NET Aspire orchestration host — entry point for local dev; provisions all backing containers |
| `src/systemd` | **Canonical** systemd units (`blazor-app.service`, `pg-dump-prune.*`) — Ansible copies them verbatim; never edit units on an LXC |
| `terraform/` | bpg/proxmox LXCs + paultyng/unifi VLANs & firewall — code mirror of `docs/04` / `docs/05` |
| `ansible/` | LXC configuration: .NET runtime, NFS mounts, systemd units, pgBackRest, Technitium DNS, step-ca, preview host |
| `deploy/preview/` | Per-PR preview compose stack template (ADR 19) — rendered by `pr-preview.yml` |
| `tests/RoadrunnerAuction.Tests` | bUnit component tests, Wolverine handler tests, Aspire integration tests |

### Deployment model

Apps are deployed to native Debian LXCs and managed strictly via `systemd`. Deploys publish into immutable release directories `/var/www/roadrunner/releases/<sha>` and atomically flip a `current` symlink (last 5 kept per node) — app rollback is a symlink move plus restart, health-gated per node so one web node always keeps serving (ADR 16). The app exposes `/health` with deep checks (Postgres, Garnet, RabbitMQ) for Kemp L7 routing — always maintain it. TLS terminates at the Kemp LoadMaster (Let's Encrypt wildcard, DNS-01); app LXCs run plain HTTP on port 5000.

### PR preview environments (non-prod)

Every open PR gets an isolated ephemeral environment on the single non-prod preview host (VLAN 40, ADR 19) — a Docker compose stack (app + pgvector + Garnet + RabbitMQ) per PR, torn down automatically on merge/close. Local-only access via Technitium wildcard DNS (`*.pr.roadrunner.internal`) and trusted HTTPS from the internal step-ca ACME CA (ADR 20) — no public exposure, no per-PR DNS/cert bookkeeping. Full guide: `docs/11-pr-preview-environments.md`. **Docker is allowed ONLY on the preview LXC** — production stays Docker-free (ADR 02).

### Admin plane (ADR 21)

Every LXC runs **Cockpit** (`https://<host>.roadrunner.internal:9090`) with a step-ca-signed cert distributed by Ansible (renewal = re-run the playbook). The preview host also runs an always-on ops stack — **Portainer, Dozzle, Watchtower (ops containers only), pgAdmin, RedisInsight** — behind Caddy at `<service>.roadrunner.internal`. Technitium serves the `roadrunner.internal` zone: one A record per LXC + service CNAMEs. pgAdmin/RedisInsight reach prod Postgres/Garnet through two targeted firewall exceptions (`terraform/unifi.tf`); nothing here is publicly exposed.

### Messaging

Wolverine (ADR 07) backs all async messaging over RabbitMQ; handlers are plain static classes discovered automatically — test them by direct method invocation. The transport can be swapped to Azure Service Bus / SQS purely via configuration.

### Data & scale-out

PostgreSQL (pgvector) is the store; Garnet is both cache and the Blazor Server SignalR backplane (`AddStackExchangeRedis`), with Kemp sticky sessions anchoring circuits to a node (ADR 08). EF Core Migration Bundles run in CI **before** app deploys — never `Database.Migrate()` on boot (ADR 11).

### Secrets & observability

No secrets in `appsettings.json` and no SDK in the app (ADR 12): the Infisical Agent renders `/etc/roadrunner/roadrunner.env`, loaded by systemd via `EnvironmentFile=`; the app fails fast if connection strings are missing. Telemetry is OpenTelemetry over OTLP (`UseOtlpExporter` driven by `OTEL_EXPORTER_OTLP_ENDPOINT`) — Grafana Alloy on VLAN 30 in production, the Aspire Dashboard locally (ADR 09).

## Network topology

Static IPs outside DHCP ranges (docs/05). Cluster nodes: **`pve4` (Node 1 - Primary: 8 vCPU / 16 GB RAM)** & **`pve3` (Node 2 - Secondary: 4 vCPU / 8 GB RAM)**. **Keep this matrix, `docs/04`, and `terraform/lxc.tf` in sync.**

### VLAN 10 — Web / Ingress (`10.10.10.x`)
| Host | IP | Node Assignment |
|------|----|-----------------|
| Synology NAS | `10.10.10.90` | External Storage |
| Cloudflared Tunnel LXC | `10.10.10.5` | `pve4` (Node 1 - Primary) |
| Kemp LoadMaster VIP (sticky sessions, LE wildcard terminated here) | `10.10.10.199` | Hardware / Appliance |
| Blazor Web 01 (Primary Web App) | `10.10.10.101` | `pve4` (Node 1 - Primary) |
| Blazor Web 02 (Secondary Web App) | `10.10.10.102` | `pve3` (Node 2 - Secondary) |

### VLAN 20 — Backend / Data (`10.10.20.x`)
| Host | IP | Node Assignment |
|------|----|-----------------|
| PostgreSQL (pgvector) | `10.10.20.110` | `pve4` (Node 1 - Primary) |
| Microsoft Garnet Cache | `10.10.20.111` | `pve4` (Node 1 - Primary) |
| RabbitMQ | `10.10.20.112` | `pve4` (Node 1 - Primary) |

### VLAN 30 — Management / Infrastructure (`10.10.30.x`)
| Host | IP | Node Assignment |
|------|----|-----------------|
| Proxmox Node 1 (`pve4` - Primary) | `10.10.30.10` | Proxmox VE Host |
| Proxmox Node 2 (`pve3` - Secondary) | `10.10.30.11` | Proxmox VE Host |
| Infisical (Admin Portal) | `10.10.30.116` | `pve4` (Node 1 - Primary) |
| Uptime Kuma | `10.10.30.117` | `pve3` (Node 2 - Secondary) |
| Grafana Loki / Observability | `10.10.30.118` | `pve3` (Node 2 - Secondary) |
| Technitium DNS (wildcard `*.pr.roadrunner.internal`) | `10.10.30.119` | `pve3` (Node 2 - Secondary) |
| step-ca internal PKI (ACME, port 4443) | `10.10.30.121` | `pve3` (Node 2 - Secondary) |

### VLAN 40 — Non-Prod / Preview (`10.10.40.x`)
| Host | IP | Node Assignment |
|------|----|-----------------|
| PR Preview host (Single Docker Host + Caddy + Ops Stack) | `10.10.40.120` | `pve4` (Node 1 - Primary) |

## Infrastructure as Code (ADR 17)

The whole lab is `terraform apply && ansible-playbook site.yml` — see `docs/08-infrastructure-as-code.md`:

- **Terraform** (`terraform/`): `bpg/proxmox` for the 12 LXCs, `paultyng/unifi` for the VLAN 10/20/30/40 networks and the LAN IN firewall matrix. `lxc.tf` / `unifi.tf` are code mirrors of `docs/04` / `docs/05` — change all three together. Apply renders the Ansible inventory.
- **Ansible** (`ansible/`): converges the web nodes (dotnet-runtime, nfs-mounts, blazor-app), the Postgres node (nfs-mounts, pgBackRest + pg-dump-prune), the preview infrastructure (technitium DNS, step-ca PKI, resolver, docker + preview-host incl. the ops stack on VLAN 40), and fleet-wide Cockpit (`hosts: all`, runs last — needs the certs the step-ca play fetches). Units are copied verbatim from `src/systemd/` — edit them there and re-run the playbook.
- **Kemp LoadMaster** remains GUI-managed (no supported Terraform provider).

## CI/CD & deployments

- **PR previews:** `pr-preview.yml` (on PR open/sync, self-hosted runner, `preview` environment) deploys an isolated compose stack per PR to the preview host (`10.10.40.120`); `pr-preview-cleanup.yml` (on PR close) tears it down (`docker compose down -v` + Caddy site removal). Details in `docs/11`.
- **Migrations:** GitHub Actions MUST generate and execute EF Core Migration Bundles BEFORE deploying web apps to prevent race conditions.
- **Versioning:** Semantic versioning (MAJOR.MINOR.PATCH) with `version.txt` as the source of truth for MAJOR.MINOR. On every PR opening/reopening against `main`, `bump-minor.yml` auto-bumps the MINOR by 1 (relative to `main`'s current version) and pushes a commit. On deploy (`deploy-blazor.yml`), the PATCH counter is auto-incremented via a persistent state file on the self-hosted runner (`~/.roadrunner/deploy-build-state`) — it resets to 0 when MAJOR.MINOR changes. The full `X.Y.Z` version is injected into the binary via `/p:Version=X.Y.Z` and read at runtime by `VersionService` (from `AssemblyInformationalVersionAttribute`). To manually seed/reset the state file:
  ```bash
  mkdir -p ~/.roadrunner
  printf 'MAJOR_MINOR=1.0\nPATCH=0\n' > ~/.roadrunner/deploy-build-state
  ```
- **Rollback:** `rollback.yml` (manual, `production` approval + typed `RESTORE` confirmation) flips symlinks per node and can restore the pre-migration `pg_dump` into a fresh database with a non-destructive RENAME-swap — nothing is ever DROPed. pgBackRest continuous WAL archiving to the NAS provides PITR (~60s max loss) — procedure in `docs/10-rollback.md` section 4.

## C# Coding Conventions

Follow these patterns consistently:

- **File-scoped namespaces** always — `namespace Foo.Bar;` not block-scoped
- **Primary constructors** preferred for DI: `public class Svc(ILogger<Svc> logger) { private readonly ILogger<Svc> _logger = logger; }`
- **Collection expressions**: `CultureInfo[] cultures = [new("en-US"), new("es")];`
- **Null checks**: `is null` / `is not null`, never `== null`
- **Async methods**: always suffix `Async`; always include `CancellationToken cancellationToken = default`
- **Records** for DTOs; properties use `{ get; set; }` auto-props
- Nullable reference types enabled — annotate all nullable members with `?`

## Configuration

Local development needs no manual config — the AppHost injects all connection strings. In production the required environment keys (rendered by the Infisical Agent) are: `ConnectionStrings__roadrunnerdb`, `ConnectionStrings__cache`, `ConnectionStrings__messaging`, `OTEL_EXPORTER_OTLP_ENDPOINT`. GitHub Actions needs the `EFBUNDLE_CONNECTION` secret (real Postgres connection string for the migration bundle) and a `production` environment with a required reviewer. Never put credentials in workflow files.

## Safety guardrails (RED RULES)

- **LXC Deletion:** never run `pct destroy <vmid>` — and treat `terraform destroy` on an LXC the same way.
- **Data Wipe:** never run `rm -rf` on any directory mapped to a Synology NAS.
- **Database Drops:** never execute `DROP DATABASE` or `DROP TABLE` without explicit confirmation.
