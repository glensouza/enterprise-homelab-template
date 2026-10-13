#!/usr/bin/env bash
# Fetch the homelab's step-ca root certificate, verify it against the CA host, and trust it on macOS or Linux.
# Counterpart of scripts/trust-homelab-ca.ps1 (Windows). See docs/11 section 4 and docs/01 ADR 126.
#
# Every internal HTTPS name (authentik/critterwatch/grafana/... .brewhouse.internal) is signed by the step-ca
# root, so a browser shows ERR_CERT_AUTHORITY_INVALID until that root is trusted. Steps:
#   1. scp the public root certificate from devops
#   2. compare its SHA-256 fingerprint with the one the step-ca host itself reports (refuse to install on a mismatch)
#   3. install it: macOS System keychain, or the Linux system store (Debian/Ubuntu, Fedora/RHEL family),
#      plus the per-user NSS database that Chrome/Chromium/Firefox-on-Linux read, when `certutil` is available
#
# Usage: scripts/trust-homelab-ca.sh [--devops IP] [--step-ca IP] [--out FILE] [--dry-run] [--no-nss]
#   --dry-run  fetch and verify only; install nothing (needs no sudo)
# Defaults match this repo's addressing (devops 10.10.10.111, step-ca 10.10.130.121); override for your own lab.
# Needs: ssh/scp access to root@devops, openssl, and sudo for the install step.

set -euo pipefail

DEVOPS="${DEVOPS:-10.10.10.111}"
STEP_CA="${STEP_CA:-10.10.130.121}"
REMOTE_ROOT="${REMOTE_ROOT:-/opt/ansible-fetched/step-ca/root_ca.crt}"
CERT_NAME="BrewHouse Homelab CA Root CA"
OUT=""
DRY_RUN=0
NO_NSS=0

die() { echo "error: $*" >&2; exit 1; }

# "SHA256 Fingerprint=AA:BB:..." (OpenSSL / LibreSSL) -> "AABB..." (upper case, no colons)
normalize_fingerprint() {
  sed -e 's/.*=//' -e 's/://g' | tr -d '[:space:]' | tr '[:lower:]' '[:upper:]'
}

cert_fingerprint() { # <file>
  openssl x509 -in "$1" -noout -fingerprint -sha256 | normalize_fingerprint
}

detect_os() {
  case "$(uname -s)" in
    Darwin) echo macos ;;
    Linux)
      if command -v update-ca-certificates >/dev/null 2>&1; then echo debian
      elif command -v update-ca-trust >/dev/null 2>&1; then echo redhat
      else echo linux-unknown
      fi ;;
    *) echo unsupported ;;
  esac
}

parse_args() {
  while [ $# -gt 0 ]; do
    case "$1" in
      --devops)  DEVOPS="${2:?--devops needs a value}"; shift 2 ;;
      --step-ca) STEP_CA="${2:?--step-ca needs a value}"; shift 2 ;;
      --out)     OUT="${2:?--out needs a value}"; shift 2 ;;
      --dry-run) DRY_RUN=1; shift ;;
      --no-nss)  NO_NSS=1; shift ;;
      -h|--help) sed -n '2,20p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
      *) die "unknown argument: $1 (try --help)" ;;
    esac
  done
  if [ -z "$OUT" ]; then
    if [ -d "$HOME/Downloads" ]; then OUT="$HOME/Downloads/brewhouse-root_ca.crt"
    else OUT="${TMPDIR:-/tmp}/brewhouse-root_ca.crt"
    fi
  fi
}

install_macos() {
  echo "Installing into the macOS System keychain (asks for your password) ..."
  sudo security add-trusted-cert -d -r trustRoot -k /Library/Keychains/System.keychain "$OUT"
}

install_debian() {
  echo "Installing into the Debian/Ubuntu system store ..."
  sudo install -m 0644 "$OUT" /usr/local/share/ca-certificates/brewhouse-homelab-root.crt
  sudo update-ca-certificates
}

install_redhat() {
  echo "Installing into the Fedora/RHEL system store ..."
  sudo install -m 0644 "$OUT" /etc/pki/ca-trust/source/anchors/brewhouse-homelab-root.crt
  sudo update-ca-trust extract
}

# Chrome/Chromium (and Firefox with the system policy off) on Linux read ~/.pki/nssdb, not the system store.
install_nss() {
  [ "$NO_NSS" -eq 1 ] && return 0
  [ "$(uname -s)" = Linux ] || return 0
  if ! command -v certutil >/dev/null 2>&1; then
    echo "note: 'certutil' (libnss3-tools / nss-tools) is not installed, so the browser database ~/.pki/nssdb was not updated."
    echo "      Chrome/Chromium on Linux need it: install the package and re-run, or import the file in the browser's certificate settings."
    return 0
  fi
  mkdir -p "$HOME/.pki/nssdb"
  [ -f "$HOME/.pki/nssdb/cert9.db" ] || certutil -d "sql:$HOME/.pki/nssdb" -N --empty-password
  # -A on an existing nickname fails; delete the previous copy first so a rebuilt root replaces the old one.
  certutil -d "sql:$HOME/.pki/nssdb" -D -n "$CERT_NAME" >/dev/null 2>&1 || true
  certutil -d "sql:$HOME/.pki/nssdb" -A -t "C,," -n "$CERT_NAME" -i "$OUT"
  echo "Added to ~/.pki/nssdb (Chrome/Chromium)."
}

main() {
  parse_args "$@"
  command -v openssl >/dev/null 2>&1 || die "openssl is required"

  echo "Fetching the root certificate from root@$DEVOPS ..."
  scp -q "root@${DEVOPS}:${REMOTE_ROOT}" "$OUT" || die "scp from devops failed"

  local local_fp remote_fp
  local_fp="$(cert_fingerprint "$OUT")" || die "the downloaded file is not a certificate"

  echo "Asking the CA host ($STEP_CA) for its own fingerprint ..."
  remote_fp="$(ssh "root@$DEVOPS" "ssh root@$STEP_CA 'openssl x509 -in /root/.step/certs/root_ca.crt -noout -fingerprint -sha256'" | normalize_fingerprint)" \
    || die "could not read the fingerprint from the step-ca host"
  [ -n "$remote_fp" ] || die "the step-ca host returned no fingerprint"

  echo "Downloaded : $local_fp"
  echo "CA reports : $remote_fp"
  [ "$local_fp" = "$remote_fp" ] || die "fingerprints do NOT match - not installing. Check that devops and the CA host are the ones you expect."

  if [ "$DRY_RUN" -eq 1 ]; then
    echo "Fingerprints match. --dry-run: nothing installed. The certificate is at $OUT"
    return 0
  fi

  echo "Fingerprints match."
  case "$(detect_os)" in
    macos)   install_macos ;;
    debian)  install_debian ;;
    redhat)  install_redhat ;;
    linux-unknown) die "no update-ca-certificates or update-ca-trust found - import $OUT by hand (docs/11 section 4)" ;;
    *)       die "unsupported OS $(uname -s) - on Windows use scripts/trust-homelab-ca.ps1" ;;
  esac
  install_nss
  echo "Done. Fully restart your browser so it picks up the new root."
  echo "Firefox keeps its own store on every OS: import $OUT under Settings > Privacy & Security > Certificates > View Certificates > Authorities, or enable security.enterprise_roots.enabled."
}

# Run main only when executed, so the helper functions can be sourced by a test.
if [ "${BASH_SOURCE[0]}" = "$0" ]; then
  main "$@"
fi
