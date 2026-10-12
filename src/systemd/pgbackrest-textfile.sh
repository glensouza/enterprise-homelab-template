#!/usr/bin/env bash
# Publishes the age of the newest pgBackRest backup to node_exporter's textfile
# collector (docs/01 ADR 114). Prometheus alerts on it (PgBackRestBackupStale).
# Runs as root from pgbackrest-textfile.timer; pgbackrest itself runs as postgres.
set -uo pipefail

STANZA="${PGBACKREST_STANZA:-brewhouse}"
DIR=/var/lib/prometheus/node-exporter
OUT="$DIR/pgbackrest.prom"
mkdir -p "$DIR"
TMP="$(mktemp "$DIR/.pgbackrest.XXXXXX")"
trap 'rm -f "$TMP"' EXIT

success=0
last_stop=""
if json="$(runuser -u postgres -- pgbackrest --stanza="$STANZA" --output=json info 2>/dev/null)"; then
  # Newest backup (any type) of the CURRENT cluster; after a rebuild's stanza-upgrade the
  # repo also holds the old cluster's backups, which must not hide a missing new one.
  # Empty when the cluster has none yet.
  last_stop="$(jq -r '(.[0].db | map(.id) | max) as $c | [.[0].backup[]? | select(.database.id == $c) | .timestamp.stop] | max // empty' <<<"$json" 2>/dev/null)" && success=1
fi

{
  echo '# HELP pgbackrest_textfile_scrape_success 1 if pgbackrest info was read and parsed.'
  echo '# TYPE pgbackrest_textfile_scrape_success gauge'
  echo "pgbackrest_textfile_scrape_success{stanza=\"$STANZA\"} $success"
  if [ -n "$last_stop" ]; then
    echo '# HELP pgbackrest_last_backup_timestamp_seconds Unix time the newest pgBackRest backup finished.'
    echo '# TYPE pgbackrest_last_backup_timestamp_seconds gauge'
    echo "pgbackrest_last_backup_timestamp_seconds{stanza=\"$STANZA\"} $last_stop"
  fi
} >"$TMP"
chmod 0644 "$TMP"
mv -f "$TMP" "$OUT"
trap - EXIT
