# Proxmox LXC Provisioning & Setup Guide

This guide details the exact steps and resource allocations needed to provision the infrastructure tier.

> **IaC target state (ADR 17):** the matrix below is implemented as code in `terraform/lxc.tf` and converged by `ansible/` — see `docs/08-infrastructure-as-code.md`. The community scripts in section 2 remain the one-time bootstrap for service *payloads* (PostgreSQL binaries, Cloudflared connector) that Ansible does not manage, and a fallback if Terraform is unavailable. **Keep the matrix, `terraform/lxc.tf`, and the CLAUDE.md topology in sync.**

---

## 1. Master Infrastructure Matrix

Configure your LXCs with the following specifications. **Ensure you assign the correct VLAN Tag in Proxmox Network settings upon creation.**

| Service Name | VLAN / IP Range | Proxmox Node | Cores | RAM | Synology NAS Mount Path |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Blazor Web 01** | VLAN 10 (`10.10.10.101`) | Node 1 | 2 | 1024 MB | `/volume1/media` |
| **Blazor Web 02** | VLAN 10 (`10.10.10.102`) | Node 2 | 2 | 1024 MB | `/volume1/media` |
| **PostgreSQL** | VLAN 20 (`10.10.20.110`) | Node 1 | 4 | 4096 MB | `/volume1/postgres-data` |
| **Garnet** | VLAN 20 (`10.10.20.111`) | Node 1 | 2 | 2048 MB | *None* |
| **RabbitMQ** | VLAN 20 (`10.10.20.112`) | Node 1 | 2 | 1024 MB | *None* |
| **Infisical** | VLAN 30 (`10.10.30.116`) | Node 2 | 2 | 2048 MB | *None* |
| **Cloudflared** | VLAN 10 (`10.10.10.5`)   | Node 1 | 1 | 512 MB  | *None* |
| **Uptime Kuma** | VLAN 30 (`10.10.30.117`) | Node 2 | 1 | 1024 MB | *None* |
| **Grafana Loki / Observability** | VLAN 30 (`10.10.30.118`) | Node 2 | 2 | 2048 MB | *None* |
| **Technitium DNS** | VLAN 30 (`10.10.30.119`) | Node 2 | 1 | 1024 MB | *None* |
| **step-ca (internal PKI)** | VLAN 30 (`10.10.30.121`) | Node 2 | 1 | 512 MB | *None* |
| **PR Preview (non-prod)** | VLAN 40 (`10.10.40.120`) | Node 2 | 4 | 8192 MB | *None* |

*Note: the Observability LXC hosts Grafana Alloy (OTLP receiver) + Loki + Grafana (see `docs/07-observability.md`). The Technitium DNS, step-ca, and PR Preview LXCs implement ephemeral PR environments — see `docs/11-pr-preview-environments.md` (ADR 19/20). The PR Preview LXC runs Docker (non-prod exception to ADR 02) and is firewalled off from all production tiers (VLAN 40, `docs/05`).*

---

## 2. Automated Provisioning Commands

Run these commands directly in the **Proxmox Host Shell**:

```bash
# Provision PostgreSQL LXC via Community Script
bash -c "$(wget -qLO - https://github.com/community-scripts/ProxmoxVE/raw/main/ct/postgresql.sh)"

# Provision Cloudflared Zero-Trust Tunnel
bash -c "$(wget -qLO - https://github.com/community-scripts/ProxmoxVE/raw/main/ct/cloudflared.sh)"
```

---

## 3. Scheduled Maintenance on the PostgreSQL LXC

Pre-migration `pg_dump` backups accumulate on the NAS mount; the `pg-dump-prune` timer deletes dumps older than 30 days and refuses to run if the NAS mount is down. It is installed automatically by Ansible (`ansible/roles/postgres`, see `docs/08`) along with the pgBackRest PITR timers (`docs/10` section 4). Manual install on the PostgreSQL LXC (`10.10.20.110`) if Ansible is unavailable:

```bash
cp src/systemd/pg-dump-prune.sh /usr/local/sbin/pg-dump-prune.sh
chmod +x /usr/local/sbin/pg-dump-prune.sh
cp src/systemd/pg-dump-prune.service src/systemd/pg-dump-prune.timer /etc/systemd/system/

systemctl daemon-reload && systemctl enable --now pg-dump-prune.timer
systemctl list-timers   # verify pg-dump-prune.timer is scheduled
```
