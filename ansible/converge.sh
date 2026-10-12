#!/usr/bin/env bash
# The one converge entry point for CI (ansible-converge.yml and terraform.yml, ADR 112):
#   1. site.yml --limit infisical --tags vault   bring the vault up (first three plays only)
#   2. files/bootstrap-infisical.py   admin/org/projects/identities + /opt/ansible-secrets/*.yml
#   3. site.yml                       everything else, reading the credentials step 2 wrote
# The split exists because `-e @file` values are read when ansible-playbook starts, so the
# credentials cannot be created inside the same run that consumes them.
set -euo pipefail
cd "$(dirname "$0")"
S=/opt/ansible-secrets

extra_vars() {
  local f
  for f in all dns web critterwatch pve-exporter; do
    [ -f "$S/$f.yml" ] && printf -- '-e\n@%s/%s.yml\n' "$S" "$f"
  done
}
run_site() { mapfile -t ev < <(extra_vars); ansible-playbook site.yml "${ev[@]}" "$@"; }

run_site --limit infisical --tags vault
python3 files/bootstrap-infisical.py
run_site
