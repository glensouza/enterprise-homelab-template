# Enterprise Proxmox + .NET 10 Blazor Home Lab Template

A production-grade, highly available, decentralized home lab architecture template for hosting **.NET 10 Blazor** web applications on **Proxmox VE 9.1**. 

This repository utilizes a consolidated AI-Native approach via a single `CLAUDE.md` file, teaching Claude Code your exact architecture, network layout, safety guardrails, and required developer workflow (auto-updating tests and docs).

## Repository Structure

```text
enterprise-homelab-template/
├── README.md
├── CLAUDE.md                          
├── LICENSE                            
├── enterprise-homelab-template.slnx   # Solution file
├── global.json                        # .NET SDK 10.0.300 (rollForward latestFeature)
├── Directory.Packages.props           # Central package management (net10.0)
├── docs/
│   ├── architecture.drawio            # Updated w/ Let's Encrypt & DNS-01
│   ├── 01-architecture-decisions.md   
│   ├── 02-proxmox-and-backups.md
│   ├── 03-kemp-load-balancer.md
│   ├── 04-lxc-provisioning.md
│   ├── 05-unifi-network-isolation.md  
│   ├── 06-testing-strategy.md         
│   ├── 07-observability.md            
│   ├── 08-infrastructure-as-code.md   # Terraform + Ansible implementation guide
│   ├── 09-ssl-certificates.md         # Let's Encrypt Wildcard & Cloudflare DNS-01 Strategy
│   └── 10-rollback.md                 # Symlinked releases + pg_dump + pgBackRest PITR recovery
├── src/
│   ├── RoadrunnerAuction/             
│   │   ├── RoadrunnerAuction.csproj
│   │   ├── Program.cs                 
│   │   ├── Migrations/                # EF Core migrations
│   │   ├── appsettings.json           
│   │   ├── appsettings.Development.json 
│   │   └── ... (Blazor Application Code)
│   ├── RoadrunnerAuction.AppHost/     # .NET Aspire 13 local orchestration
│   └── systemd/
│       ├── blazor-app.service        # Blazor app unit (web LXCs)
│       ├── pg-dump-prune.sh          # Deletes pg_dump backups older than RETENTION_DAYS
│       ├── pg-dump-prune.service     # Oneshot prune unit (Postgres LXC)
│       └── pg-dump-prune.timer       # Daily schedule for the prune service
├── terraform/                         # bpg/proxmox LXCs + paultyng/unifi VLANs/firewall (docs/08)
├── ansible/                           # LXC config: .NET runtime, NFS mounts, systemd, pgBackRest (docs/08)
├── tests/
│   └── RoadrunnerAuction.Tests/       
└── .github/
    └── workflows/
        ├── pr-build-test.yml          # PR restore/build/test (ubuntu-latest)
        ├── deploy-blazor.yml          # Test -> backup DB -> migrate -> symlinked release deploy
        └── rollback.yml               # Manual rollback to any of the last 5 releases
```

## Using This Template (Onboarding Checklist)

A new adopter must complete these steps before the first deployment:

1. **Network & naming:** Replace all IPs, VLAN IDs, and hostnames to match your network — `CLAUDE.md` (topology matrix), `docs/04-lxc-provisioning.md`, `docs/05-unifi-network-isolation.md`, `src/systemd/blazor-app.service`, and both deploy/rollback workflows. Replace `*.yourdomain.com` in `docs/03-kemp-load-balancer.md` and `docs/09-ssl-certificates.md`.
2. **Provision infrastructure:** run Terraform + Ansible per `docs/08` (`terraform apply && ansible-playbook site.yml`) — this creates the VLANs/firewall (`docs/05`), LXCs (`docs/04`), NFS mounts, and pgBackRest PITR. Manual GUI/CLI equivalents remain documented in `docs/02`–`docs/05`. NAS setup per `docs/02`, Kemp VIP per `docs/03`.
3. **Infisical:** Create a project on your Infisical LXC. Install the Infisical Agent on each Blazor LXC to render `/etc/roadrunner/roadrunner.env` with: `ConnectionStrings__roadrunnerdb`, `ConnectionStrings__cache`, `ConnectionStrings__messaging`, and `OTEL_EXPORTER_OTLP_ENDPOINT`. The app fails fast without them (ADR 12).
4. **systemd:** Copy `src/systemd/blazor-app.service` to `/etc/systemd/system/` on both web LXCs, then `systemctl enable --now blazor-app.service`.
5. **GitHub secrets:** Set `EFBUNDLE_CONNECTION` to the real PostgreSQL connection string used by the migration bundle. Never put credentials in workflow files.
6. **GitHub environment:** Create a `production` environment with yourself as required reviewer — every deploy and rollback then needs explicit approval.
7. **Self-hosted runner:** Register a runner with network access to the LXCs, `rsync`, and key-based SSH to the web and Postgres LXCs.
8. **Observability:** Point `OTEL_EXPORTER_OTLP_ENDPOINT` at Grafana Alloy on your observability LXC (`docs/07-observability.md`).
9. **Smoke test:** Push to `main`, approve the deployment, verify `https://app.yourdomain.com/health` reports healthy behind the Kemp VIP.

## Local Development

**Prerequisites:** Docker Desktop and the .NET 10 SDK.

```bash
dotnet run --project src/RoadrunnerAuction.AppHost
```

The .NET Aspire AppHost provisions the PostgreSQL (pgvector), Garnet, and RabbitMQ containers and injects their connection strings into the Blazor app automatically. The Aspire Dashboard shows unified logs, metrics, and traces via OpenTelemetry.

---
### Source Material & Attribution
Architectural patterns within this template are derived from best practices documented by the Microsoft .NET Foundation (Blazor/EF Core/Aspire), Grafana Labs (Observability), Wolverine (Distributed Systems), Progress Kemp (LoadMaster ACME/Let's Encrypt integration), and the Proxmox VE Community.
