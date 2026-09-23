# Enterprise Proxmox + .NET 10 Blazor Home Lab Template

A production-grade, highly available, decentralized home lab architecture template for hosting **.NET 10 Blazor** web applications on **Proxmox VE 9.1**. 

This repository utilizes a consolidated AI-Native approach via a single `CLAUDE.md` file, teaching Claude Code your exact architecture, network layout, safety guardrails, and required developer workflow (auto-updating tests and docs).

## Action Statuses

[![Ansible Converge](https://github.com/glensouza/enterprise-homelab-template/actions/workflows/ansible-converge.yml/badge.svg)](https://github.com/glensouza/enterprise-homelab-template/actions/workflows/ansible-converge.yml)
[![Bump Minor Version on PR](https://github.com/glensouza/enterprise-homelab-template/actions/workflows/bump-minor.yml/badge.svg)](https://github.com/glensouza/enterprise-homelab-template/actions/workflows/bump-minor.yml)
[![Bump Pinned Versions](https://github.com/glensouza/enterprise-homelab-template/actions/workflows/bump-pinned-versions.yml/badge.svg)](https://github.com/glensouza/enterprise-homelab-template/actions/workflows/bump-pinned-versions.yml)
[![Deploy .NET 10 Blazor App to LXC](https://github.com/glensouza/enterprise-homelab-template/actions/workflows/deploy-blazor.yml/badge.svg)](https://github.com/glensouza/enterprise-homelab-template/actions/workflows/deploy-blazor.yml)
[![Deploy CritterWatch Console](https://github.com/glensouza/enterprise-homelab-template/actions/workflows/deploy-critterwatch.yml/badge.svg)](https://github.com/glensouza/enterprise-homelab-template/actions/workflows/deploy-critterwatch.yml)
[![Patch Fleet](https://github.com/glensouza/enterprise-homelab-template/actions/workflows/patch-fleet.yml/badge.svg)](https://github.com/glensouza/enterprise-homelab-template/actions/workflows/patch-fleet.yml)
[![PR Build & Test](https://github.com/glensouza/enterprise-homelab-template/actions/workflows/pr-build-test.yml/badge.svg)](https://github.com/glensouza/enterprise-homelab-template/actions/workflows/pr-build-test.yml)
[![PR Preview Cleanup](https://github.com/glensouza/enterprise-homelab-template/actions/workflows/pr-preview-cleanup.yml/badge.svg)](https://github.com/glensouza/enterprise-homelab-template/actions/workflows/pr-preview-cleanup.yml)
[![PR Preview Deploy](https://github.com/glensouza/enterprise-homelab-template/actions/workflows/pr-preview.yml/badge.svg)](https://github.com/glensouza/enterprise-homelab-template/actions/workflows/pr-preview.yml)
[![Rollback Blazor App](https://github.com/glensouza/enterprise-homelab-template/actions/workflows/rollback.yml/badge.svg)](https://github.com/glensouza/enterprise-homelab-template/actions/workflows/rollback.yml)
[![Secret Scan](https://github.com/glensouza/enterprise-homelab-template/actions/workflows/secret-scan.yml/badge.svg)](https://github.com/glensouza/enterprise-homelab-template/actions/workflows/secret-scan.yml)
[![Terraform Apply](https://github.com/glensouza/enterprise-homelab-template/actions/workflows/terraform-apply.yml/badge.svg)](https://github.com/glensouza/enterprise-homelab-template/actions/workflows/terraform-apply.yml)
[![Terraform Plan](https://github.com/glensouza/enterprise-homelab-template/actions/workflows/terraform-plan.yml/badge.svg)](https://github.com/glensouza/enterprise-homelab-template/actions/workflows/terraform-plan.yml)

## Repository Structure

```text
enterprise-homelab-template/
├── README.md
├── CLAUDE.md                          
├── LICENSE                            
├── enterprise-homelab-template.slnx   # Solution file
├── global.json                        # .NET SDK 10.0.300 (rollForward latestFeature)
├── version.txt                        # SemVer source of truth (MAJOR.MINOR; PATCH auto-increments in CI)
├── Directory.Packages.props           # Central package management (net10.0)
├── docs/
│   ├── architecture.drawio            # Full topology incl. VLAN 140 preview tier
│   ├── 01-architecture-decisions.md   
│   ├── 02-proxmox-and-backups.md
│   ├── 03-kemp-load-balancer.md
│   ├── 04-lxc-provisioning.md
│   ├── 05-unifi-network-isolation.md  
│   ├── 06-testing-strategy.md         
│   ├── 07-observability.md            
│   ├── 08-infrastructure-as-code.md   # Terraform + Ansible implementation guide
│   ├── 09-ssl-certificates.md         # Let's Encrypt Wildcard & Cloudflare DNS-01 Strategy
│   ├── 10-rollback.md                 # Symlinked releases + pg_dump + pgBackRest PITR recovery
│   └── 11-pr-preview-environments.md  # Ephemeral per-PR envs, internal DNS/PKI, admin tooling
├── src/
│   ├── BrewHouse/             
│   │   ├── BrewHouse.csproj
│   │   ├── Dockerfile                 # PR preview image only (non-prod; prod is Docker-free)
│   │   ├── Program.cs                 
│   │   ├── Migrations/                # EF Core migrations
│   │   ├── appsettings.json           
│   │   ├── appsettings.Development.json 
│   │   └── ... (Blazor Application Code)
│   ├── BrewHouse.AppHost/     # .NET Aspire 13 local orchestration
│   ├── CritterWatch/          # JasperFx CritterWatch console - Wolverine/Marten monitoring (ADR 91)
│   └── systemd/
│       ├── blazor-app.service        # Blazor app unit (web LXCs)
│       ├── critterwatch.service      # CritterWatch console unit (critterwatch LXC)
│       ├── pg-dump-prune.sh          # Deletes pg_dump backups older than RETENTION_DAYS
│       ├── pg-dump-prune.service     # Oneshot prune unit (Postgres LXC)
│       └── pg-dump-prune.timer       # Daily schedule for the prune service
├── terraform/                         # bpg/proxmox LXCs + resnickio/unifi VLANs/firewall (docs/08)
├── ansible/                           # LXC config: runtime, NFS, systemd, pgBackRest, DNS/PKI, Cockpit (docs/08)
├── deploy/preview/                    # Per-PR preview compose template (docs/11)
├── tests/
│   └── BrewHouse.Tests/       
├── .githooks/
│   └── pre-commit                     # Local gitleaks scan on staged changes (opt-in, see below)
├── .gitleaks.toml                     # gitleaks config: default rules + *.example allowlist
└── .github/
    └── workflows/
        ├── pr-build-test.yml          # PR restore/build/test (ubuntu-latest)
        ├── secret-scan.yml            # gitleaks over full history, every push/PR to main
        ├── pr-preview.yml             # Deploy isolated preview env per PR (self-hosted, docs/11)
        ├── pr-preview-cleanup.yml     # Tear down the preview env on PR merge/close
        ├── bump-minor.yml             # Auto-bump MINOR version on PR open against main
        ├── deploy-blazor.yml          # Test -> backup DB -> migrate -> symlinked release deploy
        ├── deploy-critterwatch.yml    # Test -> publish -> symlinked release deploy (single instance)
        └── rollback.yml               # Manual rollback to any of the last 5 releases
```

## Using This Template (Onboarding Checklist)

A new adopter must complete these steps before the first deployment:

1. **Network & naming:** Replace all IPs, VLAN IDs, and hostnames to match your network — `CLAUDE.md` (topology matrix), `docs/04-lxc-provisioning.md`, `docs/05-unifi-network-isolation.md`, `src/systemd/blazor-app.service`, and the deploy/rollback/preview workflows. Replace `*.yourdomain.com` in `docs/03-kemp-load-balancer.md` and `docs/09-ssl-certificates.md`, and the internal zone `brewhouse.internal` in `ansible/inventory/group_vars/` and `docs/11` if you want a different private suffix.
2. **Provision infrastructure:** run Terraform + Ansible per `docs/08` (`terraform apply && ansible-playbook site.yml`) — this creates the VLANs/firewall (`docs/05`), LXCs (`docs/04`), NFS mounts, pgBackRest PITR, Technitium DNS, step-ca PKI, the preview host, and fleet-wide Cockpit. Manual GUI/CLI equivalents remain documented in `docs/02`–`docs/05`. NAS setup per `docs/02`, Kemp VIP per `docs/03`.
3. **Infisical:** Create a project on your Infisical LXC. Install the Infisical Agent on each Blazor LXC to render `/etc/brewhouse/brewhouse.env` with: `ConnectionStrings__brewhousedb`, `ConnectionStrings__cache`, `ConnectionStrings__messaging`, and `OTEL_EXPORTER_OTLP_ENDPOINT`. The app fails fast without them (ADR 12).
4. **systemd:** Copy `src/systemd/blazor-app.service` to `/etc/systemd/system/` on both web LXCs, then `systemctl enable --now blazor-app.service`.
5. **GitHub secrets:** Set `EFBUNDLE_CONNECTION` to the real PostgreSQL connection string used by the migration bundle. Never put credentials in workflow files.
6. **GitHub environments:** Create a `homelab` environment with yourself as required reviewer — every deploy and rollback then needs explicit approval. Also create a `preview` environment (no reviewers) for the PR preview workflows.
7. **Self-hosted runner:** Register a runner with network access to the LXCs, `rsync`, Docker CLI, `openssl`, and key-based SSH to the web, Postgres, preview (`10.10.140.120`), and PKI (`10.10.130.121`) LXCs.
8. **Internal DNS & PKI (PR previews + admin UIs):** per `docs/11` — set the Technitium admin password, create the zones (or copy `ansible/inventory/group_vars/dns/secrets.yml.example` to `secrets.yml`, git-ignored, set `technitium_api_token` there, and re-run the playbook), point client DNS at Technitium, and distribute the step-ca root certificate to client trust stores (GPO instructions included).
9. **Observability:** Point `OTEL_EXPORTER_OTLP_ENDPOINT` at Grafana Alloy on your observability LXC (`docs/07-observability.md`).
10. **Smoke test:** Push to `main`, approve the deployment, verify `https://app.yourdomain.com/health` reports healthy behind the Kemp VIP. Then open a test PR and confirm its preview URL comes up green-padlock.

## Local Development

**Prerequisites:** Docker Desktop and the .NET 10 SDK.

```bash
dotnet run --project src/BrewHouse.AppHost
```

The .NET Aspire AppHost provisions the PostgreSQL (pgvector), Garnet, and RabbitMQ containers and injects their connection strings into the Blazor app automatically. The Aspire Dashboard shows unified logs, metrics, and traces via OpenTelemetry.

## Secret Scanning

Every push/PR to `main` runs [gitleaks](https://github.com/gitleaks/gitleaks) over the full commit history (`.github/workflows/secret-scan.yml`) — the hard backstop regardless of local setup. For faster local feedback, install `gitleaks` and enable the pre-commit hook once per clone:

```bash
git config core.hooksPath .githooks
```

It scans staged changes before each commit and warns (without blocking) if `gitleaks` isn't installed locally. See `LAB-RUNBOOK.md` in the sibling video-scripts repo — "Handling secrets" — for where each category of credential actually lives (`terraform.tfvars`, `ansible/credentials/`, per-group `secrets.yml`, or nowhere in the repo at all).

---
### Source Material & Attribution
Architectural patterns within this template are derived from best practices documented by the Microsoft .NET Foundation (Blazor/EF Core/Aspire), Grafana Labs (Observability), Wolverine (Distributed Systems), Progress Kemp (LoadMaster ACME/Let's Encrypt integration), and the Proxmox VE Community.
