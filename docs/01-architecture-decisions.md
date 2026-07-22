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

## ADR 03: App-Owned Blob Storage Abstraction (IBlobStore)
* **Decision:** Define an application-owned `IBlobStore` interface with a `LocalDiskBlobStore` implementation (`System.IO` against the Synology NAS mount, configured via `BlobStorage:RootPath`). The third-party `FluentStorage` package has been removed.
* **Rationale:** FluentStorage is unmaintained. An app-owned interface is the genuinely cloud-agnostic pattern: Azure Blob / S3 / GCS adapters can be added later behind DI without changing application code.

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

## ADR 07: Message Brokering Abstraction via Wolverine
* **Decision:** Use Wolverine for all asynchronous messaging and background processing, backed by RabbitMQ in the home lab. MassTransit has been removed.
* **Rationale:** MassTransit v9 went commercial. Wolverine provides the same transport-agnostic, `IBus`-style abstraction (`IMessageBus`) while remaining free/open-source and lighter weight — handlers are plain static classes discovered automatically. If the application migrates to Azure or AWS, the RabbitMQ transport can be swapped to Azure Service Bus or Amazon SQS purely via configuration in `Program.cs`.

---

## ADR 08: SignalR WebSockets Scale-Out via Microsoft Garnet Backplane & Kemp Sticky Sessions
* **Decision:** Configure Blazor Server SignalR hubs to use Microsoft Garnet as a distributed pub/sub backplane (`AddStackExchangeRedis`), while enforcing L7 Session Persistence (Sticky Sessions) on the Kemp LoadMaster.
* **Rationale:** Blazor Server holds circuit state in application RAM. Sticky Sessions ensure a user's persistent WebSocket connection remains anchored to their assigned Blazor LXC node. If a cross-node broadcast occurs (e.g., live auction bid update), Garnet synchronizes the SignalR hubs across `Web 01` and `Web 02` instantaneously.

---
## ADR 09: Full Observability via OpenTelemetry (OTLP)
* **Decision:** Emit logs, metrics, and traces from the application via OpenTelemetry, exported over OTLP (`UseOtlpExporter` driven by `OTEL_EXPORTER_OTLP_ENDPOINT`). Serilog and `Serilog.Sinks.Grafana.Loki` have been removed.
* **Rationale:** Three signals instead of logs alone, using a vendor-neutral wire protocol. Locally the OTLP endpoint is the Aspire Dashboard; in production it is Grafana Alloy on VLAN 30, which fans out to Loki/Tempo/Prometheus. Blazor Server apps spread across multiple LXC nodes (`Web 01`, `Web 02`) make SSH/`journalctl` debugging unscalable.

---
## ADR 10: Deep Health Checks for Kemp L7
* **Decision:** Expose `/health` using `Microsoft.Extensions.Diagnostics.HealthChecks` to verify Postgres, Garnet, and RabbitMQ connectivity.
* **Rationale:** Prevents Kemp from routing traffic to a Blazor LXC that has lost backend connectivity, enabling true zero-downtime failover.

---
## ADR 11: EF Core Migration Bundles in CI/CD
* **Decision:** Generate and execute standalone EF Core Migration Bundles during the GitHub Actions deployment pipeline, instead of running `context.Database.Migrate()` on application boot.
* **Rationale:** Prevents database lock contention and schema race conditions when both HA nodes start simultaneously.

---
## ADR 12: Secrets Delivery via Infisical Agent & systemd EnvironmentFile
* **Decision:** The Infisical Agent on VLAN 30 renders `/etc/roadrunner/roadrunner.env`, which systemd loads via `EnvironmentFile=`. The `Infisical.Sdk` package has been removed from application code; the app fails fast with a descriptive error if connection strings are missing.
* **Rationale:** No SDK or authentication code lives inside the app, and the pattern works identically for any process type (not just .NET). Secrets still never appear in `appsettings.json` and remain secured within the isolated VLAN 30 Infisical vault.

---
## ADR 13: Let's Encrypt Wildcard Certificates via DNS-01 & Kemp LoadMaster
* **Decision:** Utilize the native Let's Encrypt ACMEv2 client built into the Kemp LoadMaster to automatically request and renew wildcard certificates (`*.yourdomain.com`) using the DNS-01 challenge against the Cloudflare API.
* **Rationale:** The DNS-01 challenge allows for the issuance of trusted certificates for internal, non-publicly routable networks without opening firewall ports. By terminating SSL at the Kemp LoadMaster, all Blazor backend LXCs are offloaded from certificate management, avoiding "Not Secure" browser warnings for internal admin endpoints.

---
## ADR 14: Local Orchestration via .NET Aspire AppHost
* **Decision:** Use a .NET Aspire AppHost (`src/RoadrunnerAuction.AppHost`) for local development orchestration, replacing `docker-compose.local.yml` (deleted).
* **Rationale:** Aspire provisions the PostgreSQL (pgvector), Garnet, and RabbitMQ containers with data volumes and injects connection strings into the app dynamically as `ConnectionStrings__roadrunnerdb` / `__cache` / `__messaging` — no static local configuration to drift. Integration tests reuse the exact same dependency graph via `Aspire.Hosting.Testing`, guaranteeing local dev and CI test against identical infrastructure.

---
## ADR 15: HAProxy + Keepalived as the Documented Load-Balancer Alternative
* **Decision:** Document HAProxy + Keepalived as the free, open-source alternative to the Kemp LoadMaster (ADR 05): HAProxy stick tables provide Blazor Server session persistence, and Keepalived (VRRP) provides load-balancer redundancy.
* **Rationale:** Kemp remains the current choice, but if licensing becomes an issue, HAProxy + Keepalived delivers equivalent L7 health checking, sticky sessions, and VIP failover at zero license cost.

---
## ADR 16: Rollback via Symlinked Releases, Pre-Migration pg_dump & Guarded Automated DB Restore
* **Decision:** Deploys publish into `/var/www/roadrunner/releases/<git-sha>` and atomically flip a `current` symlink (last 5 releases retained per node). The manual `rollback.yml` workflow re-points the symlink and restarts, health-gated per node. Before every EF migration bundle execution, CI takes a `pg_dump` of the database to the Synology NAS. Database restore is **automated in the same workflow** behind two human gates: `production` environment approval and a typed `RESTORE` confirmation. The restore is non-destructive — the dump is loaded into a fresh database, sanity-checked, and the databases are RENAME-swapped; the previous database is preserved as `roadrunner_db_failed_<timestamp>` and dropped only by explicit manual action after verification.
* **Rationale:** In-place `rsync --delete` deploys made the previous version unrecoverable, and forward-only migrations had no safety net. Symlink flips make app rollback a seconds-long operation; the pre-migration dump bounds database data loss to writes made after the deploy began. Automating the restore removes error-prone manual SSH during an incident, while the approval + typed-confirmation gates satisfy the RED RULE that destructive database operations never run unattended.

---
## ADR 17: Infrastructure as Code via Terraform (bpg/proxmox + paultyng/unifi) & Ansible
* **Decision:** All LXCs, UniFi VLANs, and LAN IN firewall rules are declared in `terraform/` (`bpg/proxmox` for Proxmox VE, `paultyng/unifi` for the UDM-Pro). LXC configuration — .NET runtime, NFS mounts, systemd units, pgBackRest — is converged by `ansible/` over SSH. Terraform renders the Ansible inventory on every apply, so the pipeline is `terraform apply && ansible-playbook site.yml`. systemd unit files are copied by Ansible verbatim from `src/systemd/` (one canonical copy). The Kemp LoadMaster remains GUI-managed (no supported provider).
* **Rationale:** Manual `pct` / community-script provisioning and hand-built firewall rules were the last unreproducible part of the lab and could not be reviewed in a PR. Declarative IaC makes the whole environment reproducible, idempotent, and diff-able; keeping the LXC matrix (`lxc.tf`) as a code mirror of the `docs/04` matrix enforces the docs-as-source-of-truth workflow.

---
## ADR 18: pgBackRest Continuous WAL Archiving for PITR
* **Decision:** The PostgreSQL LXC runs pgBackRest (configured by `ansible/roles/postgres`) with `archive_command` pushing WAL to a repo on the Synology NAS (`/mnt/synology/postgres-data/pgbackrest`, `archive_timeout=60s`), plus scheduled full (weekly) and differential (nightly) backups via systemd timers (`repo1-retention-full=2`, `repo1-retention-diff=7`). Point-in-time recovery (`pgbackrest --delta --type=time restore`) is documented in `docs/10` section 4. The pre-migration `pg_dump` and its prune timer are retained as a logical, schema-level belt-and-braces copy.
* **Rationale:** The ADR 16 `pg_dump` restore path loses every write between the dump and the restore — unacceptable for an auction workload with live bids. Continuous WAL archiving bounds data loss to at most ~60 seconds of in-flight transactions, effectively eliminating the rollback data-loss window, while pgBackRest's retention/expire handles repo pruning automatically.

---

## ADR 19: Ephemeral PR Preview Environments on a Single Non-Prod Docker Host
* **Decision:** Every open PR gets an isolated preview environment on a dedicated non-prod LXC (`pr-preview`, VLAN 40, `10.10.40.120`) that runs **Docker** — one compose stack per PR (`deploy/preview/docker-compose.pr.yml`: app + pgvector Postgres + Garnet + RabbitMQ, project `pr-<n>`). The `pr-preview.yml` workflow (self-hosted runner, `preview` environment) builds the image, ships it to the host, runs the EF Core migration bundle against the per-PR database, registers a Caddy site, and comments the URL on the PR. `pr-preview-cleanup.yml` (`pull_request: closed`) tears the stack down with `docker compose down -v` and removes the Caddy site. Caddy terminates TLS on 443 and reverse-proxies to each stack's loopback-only port (`6000 + <PR#>`). VLAN 40 is firewalled off from the production tiers (`terraform/unifi.tf`). This deliberately relaxes ADR 02 for non-prod only — production remains Docker-free on systemd LXCs.
* **Rationale:** Testing against real backing services before merge catches integration bugs that bUnit/handler tests cannot. Docker compose gives perfect per-PR isolation and one-command cleanup (`down -v` removes containers, networks, and the PR database volume) — something native systemd instances with shared Postgres/RabbitMQ cannot match without bespoke teardown logic. A single preview host keeps non-prod cost and sprawl minimal; the Kemp/sticky-session HA layer is prod-only and intentionally not replicated.

---

## ADR 20: Internal DNS (Technitium Wildcard) & Internal PKI (step-ca ACME) for Non-Prod
* **Decision:** A Technitium DNS LXC (VLAN 30, `10.10.30.119`) serves the private zone `pr.roadrunner.internal` with **one wildcard A record** `*.pr.roadrunner.internal → 10.10.40.120`, so every PR preview is resolvable with zero per-PR DNS churn — nothing to create or clean up on merge. A step-ca LXC (VLAN 30, `10.10.30.121:4443`) acts as the internal certificate authority with an **ACME provisioner**; Caddy on the preview host issues per-PR certificates automatically (`acme_ca` / `acme_ca_root`), validating HTTP-01/TLS-ALPN-01 challenges over the LAN. Clients trust the step-ca root certificate once (GPO/manual install). The zone uses the ICANN-reserved `.internal` TLD — guaranteed never to collide publicly or leak. Production TLS is unchanged: Let's Encrypt DNS-01 wildcard on the Kemp (ADR 13).
* **Rationale:** PR environments must never be publicly exposed, so public CAs and DNS-01 against Cloudflare are the wrong tool for non-prod. A wildcard DNS record eliminates an entire class of CI DNS bookkeeping (and its failure modes on merge). step-ca's ACME provisioner lets stock Caddy automate issuance/renewal with no plugins, and a locally-trusted root CA gives the same "green padlock" experience internally that ADR 13 delivers for production.

---

## ADR 21: Fleet-Wide Cockpit & Containerized Admin Tooling Behind Internal DNS Names
* **Decision:** Every LXC runs **Cockpit** (`:9090`) with a TLS certificate signed by the internal step-ca — issued centrally on the CA host via the JWK provisioner and distributed by Ansible (`/etc/cockpit/ws-certs.d/`), so every admin console is a green padlock for clients that trust the internal root. The preview host additionally runs an always-on **ops compose stack** (`/opt/ops/`, separate from the per-PR stacks): **Portainer** (container management), **Dozzle** (log viewer), **Watchtower** (nightly image updates, restricted to labeled ops containers — running PR stacks are never mutated), **pgAdmin 4**, and **RedisInsight**. All UIs are loopback-only behind Caddy on 443 with step-ca-issued certificates. Technitium serves a second private zone **`roadrunner.internal`**: one A record per LXC plus friendly CNAMEs (`portainer.`, `dozzle.`, `pgadmin.`, `redisinsight.`) — nothing public-facing gets a record. Two targeted firewall exceptions let the preview host's pgAdmin/RedisInsight reach production PostgreSQL (`5432`) and Garnet (`6379`); all other VLAN 40 → production traffic stays dropped.
* **Rationale:** Admin tooling deserves the same internal-PKI/DNS treatment as the app tiers — no port memorization, no certificate warnings, no public exposure. Running the admin UIs on the non-prod Docker host keeps production LXCs minimal (ADR 02) while giving operators GUI access to both environments from one place; the two port-scoped firewall exceptions are the smallest possible relaxation and are code-reviewed in `terraform/unifi.tf`. Centralized cert issuance on the CA host avoids opening ACME challenge paths into every VLAN.

---
### Source Material & Attribution
Observability decisions derive from OpenTelemetry and Grafana Labs guidelines. EF Bundle architecture follows Microsoft's Production Deployment guidelines for Entity Framework Core. PITR architecture follows pgBackRest user documentation; IaC layout follows the bpg/proxmox and paultyng/unifi provider registry specs. Internal PKI/DNS decisions follow smallstep (step-ca ACME) and Technitium DNS documentation; the `.internal` TLD choice follows ICANN's reservation for private-use applications. Admin-plane decisions follow Cockpit (ws-certs.d), Portainer, Dozzle, Watchtower, pgAdmin, and RedisInsight official documentation.
