# Proxmox LXC Provisioning & Setup Guide

This guide details the exact steps and resource allocations needed to provision the infrastructure tier.

> **IaC target state (ADR 17):** the matrix below is implemented as code in `terraform/lxc.tf` and converged by `ansible/` — see `docs/08-infrastructure-as-code.md`. Terraform now creates every LXC shell itself (including `postgresql`), so the community scripts in section 2 no longer create their own containers for anything Terraform already manages — PostgreSQL is installed manually on the Terraform-created LXC (section 2 below); the Cloudflared connector is the one payload still bootstrapped via community script (Ansible does not manage either). **Keep the matrix, `terraform/lxc.tf`, and the CLAUDE.md topology in sync.**

---

## 1. Master Infrastructure Matrix & Cluster Workload Strategy

### Proxmox Cluster Nodes:
The real cluster has **four** nodes; this template only ever schedules LXCs on two of them.
**`pve1` (`10.10.10.101`)** is the cluster master, hosting the Kemp VM and the
manually-provisioned `devops` LXC that runs Terraform, Ansible, and the GitHub Actions
self-hosted runner (ADR 22) — deliberately kept off the two nodes below so the box that can
apply infrastructure changes can never be destroyed by one. **`pve2` (`10.10.10.102`)** is a
cluster member this template doesn't use at all — neither is ever a
`proxmox_node_1`/`proxmox_node_2` target, and `proxmox_api_url` points at `pve1` since the
Proxmox API is cluster-aware. All node management IPs sit on the existing `10.10.10.0/24` LAN,
not a Terraform-managed VLAN — same as Kemp and the NAS. Note that Proxmox shared storage
(`synology-backups`, `docs/02`) is cluster-wide regardless — it mounts on all four nodes, not
just the two below, so the NAS's NFS export must allow all four node IPs.

- **`pve4` (Node 1 - Primary)**: 8 vCPU / 16 GB RAM (`10.10.10.104`) — High-capacity node hosting primary database engines (PostgreSQL, Garnet, RabbitMQ), primary web apps (Blazor Web 01), back-office admin portals (Infisical), ingress connectors (Cloudflared), and the single non-prod Docker preview host.
- **`pve3` (Node 2 - Secondary)**: 4 vCPU / 8 GB RAM (`10.10.10.103`) — Secondary utility & load-balancing node hosting secondary web app instances (Blazor Web 02), DNS (Technitium), PKI (step-ca), monitoring (Uptime Kuma), and telemetry (Observability/Loki/Grafana). CI/CD runner tasks moved off this node entirely — the GitHub Actions self-hosted runner now lives on the pve1 devops LXC (ADR 22), never bare on a hypervisor host.

### Master Allocation Table:

| Service Name | VLAN / IP Range | Target Proxmox Node | Cores | RAM | Synology NAS Mount Path | Allocation Rationale |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **Blazor Web 01** | VLAN 110 (`10.10.110.101`) | **`pve4`** (Node 1) | 2 | 1024 MB | `/volume1/homelab-media` | Primary core web application instance |
| **Blazor Web 02** | VLAN 110 (`10.10.110.102`) | **`pve3`** (Node 2) | 2 | 1024 MB | `/volume1/homelab-media` | Secondary load-balanced web app instance |
| **Cloudflared** | VLAN 110 (`10.10.110.5`)   | **`pve4`** (Node 1) | 1 | 512 MB  | *None* | Primary ingress connector / Cloudflare tunnel |
| **PostgreSQL** | VLAN 120 (`10.10.120.110`) | **`pve4`** (Node 1) | 4 | 4096 MB | `/volume1/homelab-postgres-data` | Primary database engine (PostgreSQL + pgvector) |
| **Garnet** | VLAN 120 (`10.10.120.111`) | **`pve4`** (Node 1) | 2 | 2048 MB | *None* | Primary cache & SignalR scale-out backplane |
| **RabbitMQ** | VLAN 120 (`10.10.120.112`) | **`pve4`** (Node 1) | 1 | 1024 MB | *None* | Primary message broker for Wolverine |
| **Infisical** | VLAN 130 (`10.10.130.116`) | **`pve4`** (Node 1) | 2 | 1536 MB | *None* | Back-office admin portal for secret management |
| **Uptime Kuma** | VLAN 130 (`10.10.130.117`) | **`pve3`** (Node 2) | 1 | 512 MB  | *None* | Utility monitoring container |
| **Grafana Loki / Observability** | VLAN 130 (`10.10.130.118`) | **`pve3`** (Node 2) | 2 | 2048 MB | *None* | Utility telemetry receiver (Alloy + Loki + Grafana) |
| **Technitium DNS** | VLAN 130 (`10.10.130.119`) | **`pve3`** (Node 2) | 1 | 512 MB  | *None* | Local DNS server (`brewhouse.internal`) |
| **step-ca (internal PKI)** | VLAN 130 (`10.10.130.121`) | **`pve3`** (Node 2) | 1 | 512 MB  | *None* | Utility internal Certificate Authority |
| **PatchMon** | VLAN 130 (`10.10.130.122`) | **`pve3`** (Node 2) | 1 | 1024 MB | *None* | Fleet-wide OS package/patch tracking — LXC reserved only, no role yet |
| **PR Preview (non-prod)** | VLAN 140 (`10.10.140.120`) | **`pve4`** (Node 1) | 2 | 4096 MB | *None* | Single non-prod Docker host (per-PR compose stacks + ops UIs) |

*Note: The Observability LXC hosts Grafana Alloy (OTLP receiver) + Loki + Grafana (see `docs/07-observability.md`). The Technitium DNS, step-ca, and PR Preview LXCs implement ephemeral PR environments — see `docs/11-pr-preview-environments.md` (ADR 19/20). The PR Preview LXC runs Docker (non-prod exception to ADR 02) and is firewalled off from all production tiers (VLAN 140, `docs/05`).*

**NFS mounts are host-side bind-mounts, not in-guest NFS mounts.** Confirmed live: unprivileged LXCs cannot mount NFS in-guest at all — the Proxmox `features.mount` flag alone is documented as insufficient, a kernel/namespace limitation, not a config gap. Instead:
1. Each Proxmox node that hosts an LXC needing NAS data mounts the export itself, via that node's own `/etc/fstab` (not Terraform-managed): `pve4` mounts both `10.10.10.90:/volume1/homelab-media` at `/mnt/homelab-media` and `10.10.10.90:/volume1/homelab-postgres-data` at `/mnt/homelab-postgres-data`; `pve3` mounts `homelab-media` the same way (for `blazor-web-02`).
2. `terraform/lxc.tf` bind-mounts that host path into the container via a `mount_point` block (`volume` = host path, `path` = in-container path — same paths as before, `/mnt/synology/media` and `/mnt/synology/postgres-data`, so nothing downstream — pgBackRest, `pg-dump-prune.sh`'s `mountpoint -q` check, the app's file storage config — needed to change).
3. `blazor-web-01/02` and `postgresql` are **privileged** containers (every other LXC in the fleet stays unprivileged). Confirmed live: a bind `mount_point` on an *unprivileged* container gets its permissions masked to `0000`/`nobody:nogroup` inside the guest regardless of the real ownership/mode on the host or NAS side — a kernel user-namespace safety behavior for filesystems mounted outside that namespace (`stat` showed real `0777 uid=0` on the host vs. masked `0000 uid=65534` inside the unprivileged guest). A privileged container has no separate user namespace, so this doesn't apply.

**Why Terraform authenticates to Proxmox as `root@pam` (`providers.tf`), not a scoped API token.** Confirmed live, in order: (1) setting any `features` attribute other than `nesting` is rejected for API-token auth at both create and update time; (2) creating a privileged container is rejected the same way; (3) a raw "bind"-type `mount_point` — exactly what step 2 above needs — is rejected the same way too. All three are hardcoded to `root@pam`-only regardless of the token's assigned role, and the provider's own docs confirm auth is all-or-nothing (an `api_token`, if set, takes precedence over `username`/`password`) — there's no way to keep the token for everything else and use a password only for these three containers. See ADR 17.

**Synology NFS export permissions (manual, one-time, per share):** the UniFi firewall policies (`docs/05`) only control network reachability — the NAS's own per-share NFS client allow-list (DSM: **Control Panel -> Shared Folder -> [folder] -> Edit -> NFS Permissions**) is a separate access-control layer and defaults to no access. Because the mount now happens on the Proxmox host, not the guest, the required rule is the **host's** IP, not the guest's VLAN:
- `homelab-media` — allow `10.10.10.104` (pve4) and `10.10.10.103` (pve3).
- `homelab-postgres-data` — allow `10.10.10.104` (pve4) only.
- `homelab-proxmox-backups` — allow all four Proxmox node IPs individually (`docs/02` section 1) — already covers `10.10.10.101`-`.104`, unrelated to the two rules above.

---

## 2. Automated Provisioning Commands

### PostgreSQL (on the Terraform-created LXC)

The `postgresql.sh` community script creates its **own** new LXC when run from the Proxmox
Host Shell — it has no "install into an existing container" mode, so it can't be used against
the `postgresql` LXC Terraform already provisions (`10.10.120.110`, `terraform/lxc.tf`). No
Ansible role installs PostgreSQL either (`ansible/roles/postgres` only configures pgBackRest
and the pg_dump-prune timer, assuming PostgreSQL is already running). Install it by hand, once,
after `terraform apply` has created the LXC and before the first EF Core migration bundle run
(`deploy-blazor.yml`, ADR 11):

```bash
ssh root@10.10.120.110
apt update && apt install -y postgresql postgresql-contrib

# App role + database — nothing generates this password for you; pick one now
# (openssl rand -base64 24 works well) and record it, it's shown nowhere again.
sudo -u postgres psql -c "CREATE ROLE brewhouse WITH LOGIN PASSWORD '<generated-password>';"
sudo -u postgres psql -c "CREATE DATABASE brewhouse_db OWNER brewhouse;"
```

The resulting connection string (`Host=10.10.120.110;Port=5432;Database=brewhouse_db;Username=brewhouse;Password=<generated-password>`)
is what goes into the `EFBUNDLE_CONNECTION` GitHub secret (`LAB-RUNBOOK.md`'s GitHub section).

### Cloudflared Zero-Trust Tunnel

Run directly in the **Proxmox Host Shell**:

```bash
bash -c "$(wget -qLO - https://github.com/community-scripts/ProxmoxVE/raw/main/ct/cloudflared.sh)"
```

---

## 3. Scheduled Maintenance on the PostgreSQL LXC

Pre-migration `pg_dump` backups accumulate on the NAS mount; the `pg-dump-prune` timer deletes dumps older than 30 days and refuses to run if the NAS mount is down. It is installed automatically by Ansible (`ansible/roles/postgres`, see `docs/08`) along with the pgBackRest PITR timers (`docs/10` section 4). Manual install on the PostgreSQL LXC (`10.10.120.110`) if Ansible is unavailable:

```bash
cp src/systemd/pg-dump-prune.sh /usr/local/sbin/pg-dump-prune.sh
chmod +x /usr/local/sbin/pg-dump-prune.sh
cp src/systemd/pg-dump-prune.service src/systemd/pg-dump-prune.timer /etc/systemd/system/

systemctl daemon-reload && systemctl enable --now pg-dump-prune.timer
systemctl list-timers   # verify pg-dump-prune.timer is scheduled
```
