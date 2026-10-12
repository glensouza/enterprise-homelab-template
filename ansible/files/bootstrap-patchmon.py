#!/usr/bin/env python3
"""Idempotently bootstrap PatchMon: first-run admin + fleet auto-enrollment token.

PatchMon's first-run wizard and "Settings -> Auto Enrollment -> Create Token"
were manual UI steps (ADR 34); this scripts both so a rebuild needs no human.
Runs on the Ansible controller against PatchMon's HTTP API.

Env: PATCHMON_URL (e.g. http://10.10.130.122:3000).
State: /opt/ansible-secrets/patchmon.yml (0600) holds the generated admin
credentials plus patchmon_enroll_key/secret; the patchmon-agent role loads it
at task time (include_vars), because -e @file is read once at playbook start.
"""
import json
import os
import re
import secrets
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

URL = os.environ["PATCHMON_URL"].rstrip("/")
STATE = "/opt/ansible-secrets/patchmon.yml"
TOKEN_NAME = "ansible-fleet"
ADMIN_USER = "brewhouse-admin"
ADMIN_EMAIL = "admin@brewhouse.internal"


def call(method, path, body=None, token=None):
    headers = {"Content-Type": "application/json", "X-Device-ID": "ansible-bootstrap"}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(f"{URL}/api/v1{path}", data=data, method=method, headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            raw = r.read()
            return r.status, (json.loads(raw) if raw else {})
    except urllib.error.HTTPError as e:
        raw = e.read()
        try:
            return e.code, json.loads(raw)
        except ValueError:
            return e.code, {"raw": raw[:200].decode(errors="replace")}


def load_state():
    state = {}
    if os.path.exists(STATE):
        for line in open(STATE, encoding="utf-8"):
            m = re.match(r'^(\w+):\s*"(.*)"\s*$', line)
            if m:
                state[m.group(1)] = m.group(2)
    return state


def save_state(state):
    fd = os.open(STATE, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    with os.fdopen(fd, "w", encoding="utf-8") as f:
        f.write("---\n")
        for k, v in state.items():
            f.write(f'{k}: "{v}"\n')


def wait_for_api():
    for _ in range(60):
        try:
            code, _ = call("POST", "/auth/login", {"username": "x", "password": "x"})
            if code < 500:
                return
        except Exception:
            pass
        time.sleep(5)
    sys.exit("PatchMon API did not come up")


def login(state):
    code, body = call("POST", "/auth/login",
                      {"username": state["patchmon_admin_username"],
                       "password": state["patchmon_admin_password"]})
    return body.get("token") if code == 200 else None


def main():
    wait_for_api()
    state = load_state()

    token = login(state) if "patchmon_admin_password" in state else None
    if token is None:
        state = {
            "patchmon_admin_username": ADMIN_USER,
            "patchmon_admin_password": secrets.token_urlsafe(24),
        }
        code, body = call("POST", "/auth/setup-admin", {
            "username": ADMIN_USER, "email": ADMIN_EMAIL,
            "password": state["patchmon_admin_password"],
            "firstName": "BrewHouse", "lastName": "Admin"})
        if code not in (200, 201):
            sys.exit(f"setup-admin failed ({code}): {body}; an admin already exists "
                     f"that this script has no credentials for")
        token = body.get("token") or login(state)
        if not token:
            sys.exit("could not log in as the new admin")
        save_state(state)
        print("created PatchMon admin")

    # The first-run wizard also sets the "server URL" that generated agent
    # install scripts embed (default localhost, which no agent can reach).
    u = urllib.parse.urlparse(URL)
    code, body = call("PUT", "/settings", {
        "server_protocol": u.scheme, "server_host": u.hostname,
        "server_port": u.port or (443 if u.scheme == "https" else 80)}, token=token)
    if code != 200:
        sys.exit(f"setting the PatchMon server URL failed ({code}): {body}")

    code, tokens = call("GET", "/auto-enrollment/tokens", token=token)
    if code != 200:
        sys.exit(f"listing enrollment tokens failed ({code}): {tokens}")
    items = tokens if isinstance(tokens, list) else tokens.get("tokens", [])
    existing = [t for t in items if t.get("token_name") == TOKEN_NAME]
    if existing and state.get("patchmon_enroll_key") == existing[0].get("token_key") \
            and state.get("patchmon_enroll_secret"):
        print("enrollment token already present")
        return
    for t in existing:  # secret is only shown at creation; recreate
        call("DELETE", f"/auto-enrollment/tokens/{t['id']}", token=token)

    code, body = call("POST", "/auto-enrollment/tokens", {
        "token_name": TOKEN_NAME, "max_hosts_per_day": 100,
        "allowed_ip_ranges": [], "metadata": {"integration_type": "direct-host"}},
        token=token)
    if code not in (200, 201):
        sys.exit(f"creating enrollment token failed ({code}): {body}")
    t = body.get("token", body)
    state["patchmon_enroll_key"] = t["token_key"]
    state["patchmon_enroll_secret"] = t["token_secret"]
    save_state(state)
    print("created enrollment token")


main()
