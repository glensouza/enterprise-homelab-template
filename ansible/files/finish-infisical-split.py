#!/usr/bin/env python3
"""Finish the per-service Infisical project split (docs/01 ADR 108, docs/13).

Run on the devops LXC as root, AFTER an org admin has done the three things
only an org admin can (docs/13 section 2):
  1. created the Infisical projects "BrewHouse Web" and "BrewHouse CritterWatch"
  2. created the org-level machine identity "brewhouse-provisioner" (Universal Auth)
     and added it as Admin to the main BrewHouse project and both new ones
  3. created a client secret for it (you will be prompted to paste it)

    ssh -t devops python3 /root/finish-infisical-split.py <provisioner-client-id>

Everything else is automated and idempotent: viewer identities per consumer
(each a viewer of ONLY its own project), secrets copied from the main
project's /brewhouse and /critterwatch folders into them, and the Ansible
secret files updated (originals kept as *.bak-adr108, mode 0600). Secrets are
never printed.
"""
import getpass
import json
import os
import re
import shutil
import sys
import time
import urllib.error
import urllib.request

H = "http://10.10.130.116:8080"
ORG = "ac36e656-44d7-4bd6-a9f1-35fe258a39c3"
ALL, WEB, CW = ("/opt/ansible-secrets/%s.yml" % n for n in ("all", "web", "critterwatch"))
# consumer -> (Infisical project display name, source folder in the main project)
TARGETS = {
    "brewhouse": ("BrewHouse Web", "/brewhouse"),
    "critterwatch": ("BrewHouse CritterWatch", "/critterwatch"),
}


def call(method, path, body=None, token=None):
    req = urllib.request.Request(
        H + path, method=method,
        data=json.dumps(body).encode() if body is not None else None,
        headers={"content-type": "application/json", **({"Authorization": "Bearer " + token} if token else {})})
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


def login(client_id, client_secret):
    st, r = call("POST", "/api/v1/auth/universal-auth/login", {"clientId": client_id, "clientSecret": client_secret})
    if st != 200:
        sys.exit("login failed (HTTP %s) - wrong client id/secret?" % st)
    return r["accessToken"]


def retry(fn, what, tries=6):
    for i in range(tries):
        st, r = fn()
        if st in (200, 201):
            return r
        time.sleep(2)
    sys.exit("%s failed (HTTP %s): %s" % (what, st, str(r)[:200]))


def read_var(text, key):
    m = re.search(r"^%s:\s*(.+?)\s*$" % re.escape(key), text, re.M)
    return m.group(1).strip("'\"") if m else None


def set_var(text, key, value):
    line = '%s: "%s"' % (key, value)
    if re.search(r"^%s:" % re.escape(key), text, re.M):
        return re.sub(r"^%s:.*$" % re.escape(key), lambda _: line, text, flags=re.M)
    return text.rstrip("\n") + "\n" + line + "\n"


def write_secret_file(path, text):
    if os.path.exists(path) and not os.path.exists(path + ".bak-adr108"):
        shutil.copy2(path, path + ".bak-adr108")
        os.chmod(path + ".bak-adr108", 0o600)
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    with os.fdopen(fd, "w") as f:
        f.write(text)


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    prov_id = sys.argv[1]
    prov_secret = getpass.getpass("Paste the brewhouse-provisioner client secret (input hidden): ").strip()
    tok = login(prov_id, prov_secret)
    print("logged in as brewhouse-provisioner")

    all_text = open(ALL).read()
    old_pid = read_var(all_text, "infisical_project_id")
    env = read_var(all_text, "infisical_environment")

    st, r = call("GET", "/api/v2/organizations/%s/workspaces" % ORG, token=tok)
    projects = {w["name"]: w for w in r.get("workspaces", [])}
    for name in [t[0] for t in TARGETS.values()] + ["BrewHouse"]:
        if name not in projects:
            sys.exit("project %r not visible to brewhouse-provisioner - is it Admin there? (visible: %s)" % (name, sorted(projects)))
    if projects["BrewHouse"]["id"] != old_pid:
        sys.exit("main project id mismatch with all.yml")

    st, src = call("GET", "/api/v3/secrets/raw?workspaceId=%s&environment=%s&secretPath=/" % (old_pid, env), token=tok)
    new_creds, project_ids = {}, {}
    for target, (pname, folder) in TARGETS.items():
        pid = projects[pname]["id"]
        project_ids[target] = pid
        st, ws = call("GET", "/api/v1/workspace/%s" % pid, token=tok)
        slugs = [e["slug"] for e in ws.get("workspace", {}).get("environments", [])]
        if env not in slugs:
            sys.exit("project %r has no %r environment (has %s)" % (pname, env, slugs))

        # 1) copy this consumer's secrets from the main project's folder into the dedicated project root
        st, d = call("GET", "/api/v3/secrets/raw?workspaceId=%s&environment=%s&secretPath=%s" % (old_pid, env, folder), token=tok)
        vals = {s["secretKey"]: s["secretValue"] for s in d.get("secrets", [])}
        if not vals:
            print("%-13s no secrets in main project %s - fresh rebuild? (the converge will push them into %r)" % (target, folder, pname))
        for key, value in vals.items():
            body = {"workspaceId": pid, "environment": env, "secretPath": "/", "secretValue": value}
            st, _ = call("POST", "/api/v3/secrets/raw/" + key, body, tok)
            if st == 400:
                st, _ = call("PATCH", "/api/v3/secrets/raw/" + key, body, tok)
            if st != 200:
                sys.exit("writing %s into %s failed (HTTP %s)" % (key, pname, st))
        print("%-13s copied %d secrets -> project %r" % (target, len(vals), pname))

        # 2) a viewer-only identity inside THAT project
        name = "%s-agent" % target
        st, mem = call("GET", "/api/v2/workspace/%s/identity-memberships" % pid, token=tok)
        existing = {m["identity"]["name"]: m["identity"]["id"] for m in mem.get("identityMemberships", [])}
        if name in existing:
            iid = existing[name]
        else:
            iid = retry(lambda: call("POST", "/api/v1/projects/%s/identities" % pid, {"name": name}, tok), "create " + name)["identity"]["id"]
        ua = {"accessTokenTTL": 86400, "accessTokenMaxTTL": 86400, "accessTokenNumUsesLimit": 0}
        st, _ = call("GET", "/api/v1/auth/universal-auth/identities/%s" % iid, token=tok)
        if st != 200:
            retry(lambda: call("POST", "/api/v1/auth/universal-auth/identities/%s" % iid, ua, tok), "universal-auth for " + name)
        client_id = retry(lambda: call("GET", "/api/v1/auth/universal-auth/identities/%s" % iid, token=tok), "read client id")["identityUniversalAuth"]["clientId"]
        secret = retry(lambda: call("POST", "/api/v1/auth/universal-auth/identities/%s/client-secrets" % iid,
                                    {"description": "ansible-managed (ADR 108)"}, tok), "client secret for " + name)["clientSecret"]
        retry(lambda: call("PATCH", "/api/v2/workspace/%s/identity-memberships/%s" % (pid, iid),
                           {"roles": [{"role": "viewer", "isTemporary": False}]}, tok), "set viewer role for " + name)
        new_creds[target] = (client_id, secret)

        # 3) prove the isolation before touching any Ansible file
        t2 = login(client_id, secret)
        st, mine = call("GET", "/api/v3/secrets/raw?workspaceId=%s&environment=%s&secretPath=/" % (pid, env), token=t2)
        if st != 200 or (vals and {s["secretKey"] for s in mine["secrets"]} != set(vals)):
            sys.exit("%s cannot read its own project correctly (HTTP %s)" % (name, st))
        for other_name, other in projects.items():
            if other["id"] == pid or other_name not in ("BrewHouse", *[t[0] for t in TARGETS.values()]):
                continue
            st, _ = call("GET", "/api/v3/secrets/raw?workspaceId=%s&environment=%s&secretPath=/" % (other["id"], env), token=t2)
            print("   isolation: %s -> %-24s HTTP %s %s" % (name, other_name, st, "(denied, good)" if st in (401, 403, 404) else "<-- NOT denied!"))
            if st == 200:
                sys.exit("isolation check FAILED - %s can read %s" % (name, other_name))

    # 4) Ansible secret files (backups kept, 0600)
    all_text = set_var(all_text, "infisical_provision_client_id", prov_id)
    all_text = set_var(all_text, "infisical_provision_client_secret", prov_secret)
    if "infisical_project_ids:" not in all_text:
        all_text = all_text.rstrip("\n") + "\ninfisical_project_ids:\n" + "".join(
            '  %s: "%s"\n' % (t, project_ids[t]) for t in TARGETS)
    write_secret_file(ALL, all_text)
    web_text = open(WEB).read() if os.path.exists(WEB) else ""
    web_text = set_var(set_var(web_text, "infisical_agent_client_id", new_creds["brewhouse"][0]),
                       "infisical_agent_client_secret", new_creds["brewhouse"][1])
    write_secret_file(WEB, web_text)
    cw_text = set_var(set_var("---\n", "critterwatch_agent_client_id", new_creds["critterwatch"][0]),
                      "critterwatch_agent_client_secret", new_creds["critterwatch"][1])
    write_secret_file(CW, cw_text)
    print("\nupdated %s, %s, %s (originals: *.bak-adr108)" % (ALL, WEB, CW))
    print("NEXT: run the Ansible Converge workflow; it re-renders each agent against its own project.")
    print("      Then verify, and delete the root-level copies + retire the HomeLab identity (docs/13 section 4).")


if __name__ == "__main__":
    main()
