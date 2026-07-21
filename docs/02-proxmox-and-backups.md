# Proxmox Storage & Backup Strategy

This lab utilizes a decentralized LXC approach. Virtual container disks are stored on local Proxmox NVMe/SSD storage for speed, while persistent data (Databases, Media) and container backups are routed directly to the Synology NAS.

## 1. Mounting the Synology NAS to Proxmox
Before creating LXCs, the Synology NAS must be attached to the Proxmox Datacenter.
1. In Synology DSM, enable **NFS** and create a shared folder named `proxmox-backups`.
2. In the Proxmox GUI, go to **Datacenter** -> **Storage** -> **Add** -> **NFS**.
3. **ID:** `synology-backups`
4. **Server:** `10.10.10.90` (VLAN 10)
5. **Export:** `/volume1/proxmox-backups`
6. **Content:** Select **VZDump backup file**.

## 2. Backup Execution (VZDump)
### Manual LXC Backup via CLI:
To execute a backup with zero downtime using snapshot mode and `zstd` compression:
```bash
vzdump <LXC_ID> --mode snapshot --storage synology-backups --compress zstd
```

## 3. Disaster Recovery (Restoring an LXC)
### Restore LXC from CLI:
```bash
pct restore <NEW_LXC_ID> /mnt/pve/synology-backups/dump/vzdump-lxc-<OLD_ID>-<DATE>.tar.zst --storage local-lvm
```

## 4. Database-Level Backups (Postgres LXC)
VZDump snapshots are whole-container and coarse. The PostgreSQL LXC additionally maintains database-native backups on the `/volume1/postgres-data` NFS export (mounted at `/mnt/synology/postgres-data`, installed by the Ansible `nfs-mounts` role):

- **Pre-migration `pg_dump` files** in `backups/`, pruned daily by `pg-dump-prune.timer` (30-day retention, `docs/10` section 3).
- **pgBackRest repo** in `pgbackrest/` — continuous WAL archive + full/differential backups for point-in-time recovery (`docs/10` section 4, ADR 18). No separate DSM share is needed; it lives on the same `postgres-data` export.
