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
  # Newest backup of any type (full/diff/incr); empty when the repo has none yet.
  last_stop="$(jq -r '[.[0].backup[]?.timestamp.stop] | max // empty' <<<"$json" 2>/dev/null)" && success=1
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
