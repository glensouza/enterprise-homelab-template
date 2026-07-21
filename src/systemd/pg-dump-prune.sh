#!/usr/bin/env bash
# pg-dump-prune.sh — delete pg_dump backups older than RETENTION_DAYS days.
# Runs on the PostgreSQL LXC (10.10.20.110) via pg-dump-prune.timer.
set -euo pipefail

BACKUP_DIR="${BACKUP_DIR:-/mnt/synology/postgres-data/backups}"
MOUNT_ROOT="${MOUNT_ROOT:-/mnt/synology/postgres-data}"
RETENTION_DAYS="${RETENTION_DAYS:-30}"

# Guardrails: refuse to run unless BACKUP_DIR exists AND the NAS is mounted.
if [[ ! -d "$BACKUP_DIR" ]]; then
  echo "ERROR: BACKUP_DIR '$BACKUP_DIR' does not exist." >&2
  exit 1
fi

# BACKUP_DIR is a subdirectory of the NFS export, so check the mount root.
if ! mountpoint -q "$MOUNT_ROOT"; then
  echo "ERROR: '$MOUNT_ROOT' is not a mountpoint — NAS mount is down. Aborting prune." >&2
  exit 1
fi

echo "Pruning roadrunner_db-*.sql.gz older than ${RETENTION_DAYS} days in ${BACKUP_DIR}"

deleted=$(find "$BACKUP_DIR" -maxdepth 1 -type f -name 'roadrunner_db-*.sql.gz' -mtime +"$RETENTION_DAYS" -print -delete | wc -l)
remaining=$(find "$BACKUP_DIR" -maxdepth 1 -type f -name 'roadrunner_db-*.sql.gz' | wc -l)

echo "Deleted: ${deleted} file(s). Remaining backups: ${remaining}."
