#!/usr/bin/env bash
# ADR 40: takes a fresh UniFi controller config backup and saves it OUTSIDE
# any git checkout (ADR 35 pattern) before terraform apply ever touches
# unifi_network/unifi_firewall_policy resources. Confirmed live this
# session: a home-network-wide outage forced restoring a 2-week-old backup
# because no fresher one existed - this exists so that's never true again.
#
# Requires UNIFI_API_URL, UNIFI_USERNAME, UNIFI_PASSWORD in the environment
# (same TF_VAR_unifi_* values terraform itself uses - see terraform-apply.yml
# and terraform.tfvars). Exits non-zero on any failure, on purpose: this is
# meant to be run as a pre-apply CI/wrapper step that BLOCKS the apply if
# the backup didn't actually succeed, not a best-effort side task.
set -euo pipefail

: "${UNIFI_API_URL:?set UNIFI_API_URL}"
: "${UNIFI_USERNAME:?set UNIFI_USERNAME}"
: "${UNIFI_PASSWORD:?set UNIFI_PASSWORD}"

backup_dir=/opt/unifi-backups
mkdir -p "$backup_dir"

cookie_jar=$(mktemp)
header_file=$(mktemp)
trap 'rm -f "$cookie_jar" "$header_file"' EXIT

login_status=$(curl -sk -c "$cookie_jar" -D "$header_file" -o /dev/null -w '%{http_code}' \
  -X POST "$UNIFI_API_URL/api/auth/login" \
  -H "Content-Type: application/json" \
  -d "{\"username\":\"$UNIFI_USERNAME\",\"password\":\"$UNIFI_PASSWORD\"}")

if [ "$login_status" != "200" ]; then
  echo "ERROR: UniFi login failed (HTTP $login_status) - not proceeding without a backup." >&2
  exit 1
fi

csrf_token=$(grep -i '^x-csrf-token:' "$header_file" | cut -d' ' -f2 | tr -d '\r\n')
if [ -z "$csrf_token" ]; then
  echo "ERROR: could not extract X-CSRF-Token from login response." >&2
  exit 1
fi

backup_response=$(curl -sk -b "$cookie_jar" \
  -X POST "$UNIFI_API_URL/proxy/network/api/s/default/cmd/backup" \
  -H "Content-Type: application/json" -H "X-CSRF-Token: $csrf_token" \
  -d '{"cmd":"backup","days":-1}')

backup_path=$(echo "$backup_response" | grep -o '"url":"[^"]*"' | cut -d'"' -f4)
if [ -z "$backup_path" ]; then
  echo "ERROR: backup command didn't return a download URL. Response: $backup_response" >&2
  exit 1
fi

dest="$backup_dir/unifi-$(date -u +%Y%m%dT%H%M%SZ).unf"
download_status=$(curl -sk -b "$cookie_jar" -o "$dest" -w '%{http_code}' \
  "$UNIFI_API_URL/proxy/network$backup_path")

if [ "$download_status" != "200" ] || [ ! -s "$dest" ]; then
  echo "ERROR: backup download failed (HTTP $download_status)." >&2
  rm -f "$dest"
  exit 1
fi

echo "UniFi backup saved: $dest ($(stat -c %s "$dest") bytes)"
