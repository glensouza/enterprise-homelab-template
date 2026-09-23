# Proxmox LXC Provisioning & Setup Guide

This guide details the exact steps and resource allocations needed to provision the infrastructure tier.

> **IaC target state (ADR 17):** the matrix below is implemented as code in `terraform/lxc.tf` and converged by `ansible/` — see `docs/08-infrastructure-as-code.md`. Terraform now creates every LXC shell itself (including `postgresql`), so the community scripts in section 2 no longer create their own containers for anything Terraform already manages. PostgreSQL install + database creation is now fully automated by the `postgres` Ansible role (ADR 30) as part of `ansible-playbook site.yml` — not a manual step (section 2 below is corrected accordingly); the Cloudflared connector is the one payload still bootstrapped via community script (Ansible does not manage either). **Keep the matrix, `terraform/lxc.tf`, and the CLAUDE.md topology in sync.**

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
| **PatchMon** | VLAN 130 (`10.10.130.122`) | **`pve3`** (Node 2) | 1 | 1024 MB | *None* | Fleet-wide OS package/patch tracking — server + auto-enrolled agent on every LXC (ADR 33/34) |
| **Homepage** | VLAN 130 (`10.10.130.120`) | **`pve3`** (Node 2) | 2 | 2048 MB | *None* | Fleet dashboard — auto-populated from every other host's `homepage_service` var (ADR 38) |
| **Authentik** | VLAN 130 (`10.10.130.123`) | **`pve4`** (Node 1) | 4 | 3072 MB (+3072 MB swap) | *None* | SSO — bare-metal from-source build (Rust/Go/Node/Python). Moved from pve3 and trimmed from community-scripts' 8192 MB default (ADR 63) — pve3's 8GB physical RAM couldn't actually fit an 8GB single-LXC limit; pve4 has more real headroom and this reuses the shared postgresql/garnet LXCs, so steady-state need is lighter than the self-contained-stack default assumed |
| **PR Preview (non-prod)** | VLAN 140 (`10.10.140.120`) | **`pve4`** (Node 1) | 2 | 4096 MB | *None* | Single non-prod Docker host (per-PR compose stacks + ops UIs) |

*Note: The Observability LXC hosts Grafana Alloy (OTLP receiver) + Loki + Grafana (see `docs/07-observability.md`). The Technitium DNS, step-ca, and PR Preview LXCs implement ephemeral PR environments — see `docs/11-pr-preview-environments.md` (ADR 19/20). The PR Preview LXC runs Docker (non-prod exception to ADR 02) and is firewalled off from all homelab tiers (VLAN 140, `docs/05`).*

**`homelab-media`/`homelab-postgres-data` mount via CIFS, as host-side bind-mounts — not in-guest NFS mounts, and not NFS at all.** Confirmed live, in order: (1) unprivileged LXCs cannot mount NFS in-guest at all — the Proxmox `features.mount` flag alone is documented as insufficient, a kernel/namespace limitation, not a config gap; (2) even mounted on the *host* and bind-mounted in, NFS on these two shares denied every non-root UID (`postgres`, a plain test user) regardless of unix mode bits, NFS version (v3 and v4 both tested), or POSIX ACLs (`getfacl` showed plain `rwxrwxrwx`, nothing hidden) — root had full access throughout, which pointed at a NAS-side identity check we couldn't fully diagnose or fix without DSM's own admin console. Switched to **CIFS** instead, which authenticates via an actual username/password rather than trusting a raw client UID — sidesteps the whole class of problem:
1. A dedicated DSM user (`homelab`) has Read/Write granted directly on both shares' Permissions tab (**Control Panel -> Shared Folder -> [folder] -> Edit -> Permissions**) — the NFS Permissions tab used for `homelab-proxmox-backups` (`docs/02`) doesn't apply here at all; CIFS shares are governed by this different tab.
2. Each Proxmox node that hosts an LXC needing NAS data mounts the share itself via CIFS, via that node's own `/etc/fstab` (not Terraform-managed, needs `cifs-utils` installed): `pve4` mounts both `//10.10.10.90/homelab-media` at `/mnt/homelab-media` and `//10.10.10.90/homelab-postgres-data` at `/mnt/homelab-postgres-data`; `pve3` mounts `homelab-media` the same way (for `blazor-web-03`). Credentials live in `/etc/cifs-credentials/homelab` (`chmod 600`, not Terraform/git-managed — same "lives on disk, never committed" pattern as `terraform.tfvars`), referenced via the mount's `credentials=` option; `file_mode=0777,dir_mode=0777` makes the mount usable by every local UID once authenticated, matching how these shares were used before this switch.
3. `terraform/lxc.tf` bind-mounts that host path into the container via a `mount_point` block (`volume` = host path, `path` = in-container path — same paths as before, `/mnt/synology/media` and `/mnt/synology/postgres-data`, so nothing downstream — pgBackRest, `pg-dump-prune.sh`'s `mountpoint -q` check, the app's file storage config — needed to change).
4. `blazor-web-03/04` and `postgresql` are **privileged** containers (every other LXC in the fleet stays unprivileged) — this is independent of the NFS/CIFS choice above and still required: a bind `mount_point` on an *unprivileged* container gets its permissions masked to `0000`/`nobody:nogroup` inside the guest regardless of the real ownership/mode on the host share — a kernel user-namespace safety behavior for filesystems mounted outside that namespace, not specific to any one network filesystem. A privileged container has no separate user namespace, so this doesn't apply.

**Why Terraform authenticates to Proxmox as `root@pam` (`providers.tf`), not a scoped API token.** Confirmed live, in order: (1) setting any `features` attribute other than `nesting` is rejected for API-token auth at both create and update time; (2) creating a privileged container is rejected the same way; (3) a raw "bind"-type `mount_point` — exactly what step 3 above needs — is rejected the same way too. All three are hardcoded to `root@pam`-only regardless of the token's assigned role, and the provider's own docs confirm auth is all-or-nothing (an `api_token`, if set, takes precedence over `username`/`password`) — there's no way to keep the token for everything else and use a password only for these three containers. See ADR 17.

**`homelab-proxmox-backups` is unaffected** — it's still plain NFS with the NFS Permissions rule (`docs/02` section 1) allowing all four Proxmox node IPs, since it's exclusively root-driven VZDump traffic and never hit the non-root-UID problem above.

---

## 2. Automated Provisioning Commands

### PostgreSQL (on the Terraform-created LXC) — fully automated, no manual step

The `postgresql.sh` community script creates its **own** new LXC when run from the Proxmox
Host Shell — it has no "install into an existing container" mode, so it can't be used against
the `postgresql` LXC Terraform already provisions (`10.10.120.110`, `terraform/lxc.tf`).
**This used to require a manual install-by-hand step here — it no longer does (ADR 30).** The
`postgres` Ansible role now installs PostgreSQL itself as its first task, then creates the
`brewhouse` role/database, then (ADR 42) configures `listen_addresses`/`pg_hba.conf` for the
exact remote clients that need it (the app's own runtime connection, the preview host's
pgAdmin, and the CI runner's EF migration bundle) — the Debian package default only accepts
`127.0.0.1`/`::1`, which silently blocked all three for months until a deploy finally got far
enough to hit it (confirmed live). All of this happens automatically as part of
`ansible-playbook site.yml` (`LAB-RUNBOOK.md` §1) — nothing to do here before or after it. The
generated `brewhouse` password lives at `/opt/ansible-credentials/postgres/brewhouse_password`
on the devops LXC and in Infisical as `ConnectionStrings__brewhousedb`; both `EFBUNDLE_CONNECTION`
and `ConnectionStrings__brewhousedb` are pushed automatically by the same role.

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
