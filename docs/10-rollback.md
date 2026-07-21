# Rollback & Recovery Procedures

This document covers how to revert a bad deployment. See ADR 16 (rollback/restore) and ADR 18 (pgBackRest PITR) in `01-architecture-decisions.md` for the rationale.

---

## 1. How Deploys Are Laid Out

Each deploy publishes into an immutable release directory and atomically flips a symlink:

```text
/var/www/roadrunner/
├── releases/
│   ├── <git-sha-a>/      # previous release
│   ├── <git-sha-b>/      # current release
│   └── ...               # last 5 releases retained per node
└── current -> releases/<git-sha-b>/
```

The systemd unit runs from `/var/www/roadrunner/current`, so rollback never modifies files — it only moves the symlink and restarts the service.

## 2. App Rollback (Automated)

Use the **Rollback Blazor App** workflow (`rollback.yml`, `workflow_dispatch`) in GitHub Actions:

1. Find the target SHA: `git log --oneline` (must be one of the last 5 deployed releases).
2. Run the workflow with the full SHA. It requires `production` environment approval.
3. The workflow rolls back LXC 01 first, verifies `/health`, and only then touches LXC 02 — one node always stays serving.

### Manual equivalent (if CI is unavailable)

```bash
ssh root@10.10.10.101
ln -sfn /var/www/roadrunner/releases/<SHA> /var/www/roadrunner/current
systemctl restart blazor-app.service
curl -fsS http://localhost:5000/health
# repeat on 10.10.10.102 after verifying 01 is healthy
```

## 3. Database Rollback (Automated, Double-Gated)

Every deploy takes a `pg_dump` **before** running EF migrations:

```text
/mnt/synology/postgres-data/backups/roadrunner_db-<sha8>-<timestamp>.sql.gz   (on the Postgres LXC's NAS mount)
```

### Automated restore via `rollback.yml` (primary path)

Run the **Rollback Blazor App** workflow with:
1. `sha` — the release to roll back to (its schema must match the dump).
2. `restore_database` — `true`.
3. `backup_file` — the exact `*.sql.gz` dump filename.
4. `confirm` — type `RESTORE`.

The workflow requires `production` environment approval, then: stops both app services → verifies the dump exists → restores into a **fresh** database (`roadrunner_db_restore_<ts>`) → sanity-checks it has tables → **RENAME-swaps** the databases (nothing is ever `DROP`ed; the old database is preserved as `roadrunner_db_failed_<ts>`) → flips the release symlinks and restarts, health-gated per node.

After verifying the system, drop `roadrunner_db_failed_<ts>` manually — that final destructive step is intentionally left to a human.

### Manual fallback (if CI is unavailable)

1. Roll back the **app first** (section 2) so the running code matches the old schema.
2. Stop both app services: `ssh root@10.10.10.10{1,2} systemctl stop blazor-app.service`
3. On the Postgres LXC, restore into a fresh database and swap:
```bash
ssh root@10.10.20.110
sudo -u postgres createdb roadrunner_db_restore
gunzip -c /mnt/synology/postgres-data/backups/roadrunner_db-<sha8>-<timestamp>.sql.gz | sudo -u postgres psql roadrunner_db_restore
# VERIFY the restored data before proceeding.
# Only after verification, explicitly rename the databases:
sudo -u postgres psql -c 'ALTER DATABASE roadrunner_db RENAME TO roadrunner_db_failed;'
sudo -u postgres psql -c 'ALTER DATABASE roadrunner_db_restore RENAME TO roadrunner_db;'
```

4. Start the app services and verify `/health`.
5. Keep `roadrunner_db_failed` until you are confident, then drop it manually.

> **Data loss window:** the `pg_dump` restore loses anything written between the dump and the restore. That window is closed by pgBackRest continuous WAL archiving (section 4) — prefer PITR when you need to recover writes made after the dump. For an auction workload, consider pausing bidding (Kemp maintenance page) during either restore.

## 4. Point-in-Time Recovery (pgBackRest)

pgBackRest runs on the PostgreSQL LXC (`10.10.20.110`) with continuous WAL archiving to the Synology NAS (`/mnt/synology/postgres-data/pgbackrest`), configured by Ansible (`ansible/roles/postgres`, see `docs/08`). Because PostgreSQL pushes every WAL segment to the repo within `archive_timeout` (60s), the data-loss window shrinks from *"everything since the last dump"* to **at most ~1 minute of in-flight transactions** — for practical purposes the rollback data-loss window is eliminated.

- **Schedule:** full backup weekly (Sun 02:30), differential nightly (Mon–Sat 02:30) via the `pgbackrest-full`/`pgbackrest-diff` systemd timers; WAL archives continuously.
- **Retention:** 2 full + 7 differential backups (`repo1-retention-full/diff` in `/etc/pgbackrest/pgbackrest.conf`); older backups are expired automatically. Both timers refuse to run if the NAS mount is down (`ConditionPathIsMountPoint=`).
- **Verify:** `sudo -u postgres pgbackrest --stanza=roadrunner info` shows backups and archive ranges.

### PITR restore procedure (manual, double-gated by a human)

Use this to recover to any point in time — e.g. just before a bad migration ran — instead of the RENAME-swap in section 3:

1. Stop both app services: `ssh root@10.10.10.10{1,2} systemctl stop blazor-app.service`
2. On the Postgres LXC, stop PostgreSQL and restore to the target time:
```bash
ssh root@10.10.20.110
systemctl stop postgresql
sudo -u postgres pgbackrest --stanza=roadrunner --delta \
  --type=time --target="2026-07-21 14:30:00" \
  --target-action=promote restore
systemctl start postgresql
```
   (`--delta` only rewrites changed files. `--type=immediate` recovers to end-of-WAL if the goal is simply "as recent as possible"; `--type=xid`/`--type=name` target a transaction or restore point.)
3. Sanity-check the data, then start the app services and verify `/health` per node.

> **RED RULE note:** `--delta` restores in place. The pre-migration `pg_dump` from section 3 remains the belt-and-braces logical copy, and the failed schema/database is preserved by the section 3 RENAME-swap whenever that path is used — PITR never requires dropping anything.

## 5. Whole-Container Recovery (Last Resort)

If an LXC itself is broken, restore the latest vzdump snapshot per `docs/02-proxmox-and-backups.md`. This is coarse-grained and independent of app releases.

## 6. Release Retention & Cleanup

- **App releases:** last 5 per node, pruned automatically by the deploy workflow.
- **DB dumps:** pruned automatically by the `pg-dump-prune` systemd timer on the Postgres LXC (`10.10.20.110`) — runs daily, deletes `roadrunner_db-*.sql.gz` older than 30 days, and refuses to run if the NAS mount is down. See `src/systemd/pg-dump-prune.{sh,service,timer}`; retention is configurable via the service `Environment=RETENTION_DAYS=` (installed by Ansible, `docs/04-lxc-provisioning.md` section 3).
- **pgBackRest repo:** pruned automatically by pgBackRest itself — `repo1-retention-full=2` full + `repo1-retention-diff=7` differential backups are kept; WAL older than the oldest retained backup is expired on each successful backup.
