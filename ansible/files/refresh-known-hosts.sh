#!/usr/bin/env bash
# Re-learns the SSH host key of every inventory host on the devops runner (docs/01 ADR 117).
# A rebuild gives every LXC a new host key; the runner's known_hosts keeps the old ones, so
# workflows that ssh with strict checking (pr-preview.yml, pr-preview-cleanup.yml, ...) fail with
# "Host key verification failed". Run at the end of every converge (converge.sh): the hosts are
# up by then and Terraform has just created them, so trusting the key they present is the same
# trust decision `StrictHostKeyChecking=accept-new` would make.
set -uo pipefail
cd "$(dirname "$0")/.."
KH="${KNOWN_HOSTS:-$HOME/.ssh/known_hosts}"
mkdir -p "$(dirname "$KH")"
touch "$KH"

ips="$(ansible-inventory --list 2>/dev/null | python3 -c '
import json, sys
hv = json.load(sys.stdin)["_meta"]["hostvars"]
print("\n".join(sorted({v["ansible_host"] for v in hv.values() if "ansible_host" in v})))')"

for ip in $ips; do
  ssh-keygen -R "$ip" -f "$KH" >/dev/null 2>&1
  keys="$(ssh-keyscan -T 5 -t ecdsa,ed25519,rsa "$ip" 2>/dev/null)"
  if [ -n "$keys" ]; then
    printf '%s\n' "$keys" >>"$KH"
  else
    echo "refresh-known-hosts: no SSH answer from $ip (skipped)"
  fi
done
