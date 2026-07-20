# Proxmox LXC Provisioning & Setup Guide

This guide details the exact steps and resource allocations needed to provision the infrastructure tier using official community scripts.

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
| **Zot Registry**| VLAN 30 (`10.10.30.115`) | Node 2 | 2 | 2048 MB | `/volume1/zot-artifacts` |
| **Infisical** | VLAN 30 (`10.10.30.116`) | Node 2 | 2 | 2048 MB | *None* |
| **Cloudflared** | VLAN 10 (`10.10.10.5`)   | Node 1 | 1 | 512 MB  | *None* |
| **Uptime Kuma** | VLAN 30 (`10.10.30.117`) | Node 2 | 1 | 1024 MB | *None* |

---

## 2. Automated Provisioning Commands

Run these commands directly in the **Proxmox Host Shell**:

```bash
# Provision PostgreSQL LXC via Community Script
bash -c "$(wget -qLO - https://github.com/community-scripts/ProxmoxVE/raw/main/ct/postgresql.sh)"

# Provision Cloudflared Zero-Trust Tunnel
bash -c "$(wget -qLO - https://github.com/community-scripts/ProxmoxVE/raw/main/ct/cloudflared.sh)"
```
