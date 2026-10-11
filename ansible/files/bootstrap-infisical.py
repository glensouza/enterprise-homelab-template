#!/usr/bin/env python3
"""Bootstrap Infisical end to end: admin account, org, projects, identities, secret files
(docs/01 ADR 108/112, docs/13). Idempotent and safe to re-run; runs on the devops LXC.

Replaces the old UI steps + finish-infisical-split.py. State machine:

  provisioner credentials in all.yml still log in  -> nothing to bootstrap, just repair/verify
  instance not initialized                         -> POST /api/v1/admin/bootstrap using
                                                      INFISICAL_ADMIN_EMAIL / _PASSWORD, then
                                                      create projects + brewhouse-provisioner
  initialized, resume file present                 -> continue a previous half-finished run
  initialized, no usable credentials, no resume    -> stop and say how to recover (UI, docs/13)

Then, with the provisioner: a viewer-only identity inside each consumer project, an isolation
proof (each can read only its own project), and the Ansible secret files
/opt/ansible-secrets/{all,web,critterwatch}.yml (0600). Secrets are never printed.

The bootstrap call is one-shot (its identity token is shown once), so the token and every id
created so far are saved to /opt/ansible-secrets/infisical-bootstrap.json (0600) immediately
and the file is deleted only after everything succeeded.
"""
import json
import os
import re
import sys
import time
import urllib.error
import urllib.request

H = os.environ.get("INFISICAL_URL", "http://10.10.130.116:8080")
ORG_NAME = os.environ.get("INFISICAL_ORG_NAME", "BrewHouse")
ENV = os.environ.get("INFISICAL_ENVIRONMENT", "dev")
S = "/opt/ansible-secrets"
ALL, WEB, CW = ("%s/%s.yml" % (S, n) for n in ("all", "web", "critterwatch"))
RESUME = S + "/infisical-bootstrap.json"
MAIN = "BrewHouse"
# consumer -> Infisical project display name
TARGETS = {"brewhouse": "BrewHouse Web", "critterwatch": "BrewHouse CritterWatch"}
PROV = "brewhouse-provisioner"


def call(method, path, body=None, token=None):
    req = urllib.request.Request(
        H + path, method=method,
        data=json.dumps(body).encode() if body is not None else None,
        headers={"content-type": "application/json", **({"Authorization": "Bearer " + token} if token else {})})
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            raw = r.read()
            return r.status, (json.loads(raw) if raw else {})
    except urllib.error.HTTPError as e:
        raw = e.read()
        try:
            return e.code, json.loads(raw)
        except ValueError:
            return e.code, {"raw": raw[:200].decode(errors="replace")}
    except (urllib.error.URLError, OSError) as e:
        return 0, {"raw": str(e)}


def retry(fn, what, tries=8, ok=(200, 201)):
    st, r = 0, {}
    for _ in range(tries):
        st, r = fn()
        if st in ok:
            return r
        time.sleep(3)
    sys.exit("%s failed (HTTP %s): %s" % (what, st, str(r)[:300]))


def first_ok(attempts, what, tolerate=()):
    """Try alternative API shapes (Infisical renames routes between releases)."""
    last = (0, {})
    for method, path, body, tok in attempts:
        st, r = call(method, path, body, tok)
        if st in (200, 201):
            return r
        if st == 400 and any(t in json.dumps(r).lower() for t in tolerate):
            return r
        last = (st, r)
    sys.exit("%s failed (HTTP %s): %s" % (what, last[0], str(last[1])[:300]))


def login(client_id, client_secret):
    st, r = call("POST", "/api/v1/auth/universal-auth/login", {"clientId": client_id, "clientSecret": client_secret})
    return r.get("accessToken") if st == 200 else None


def read_var(text, key):
    m = re.search(r"^%s:\s*(.+?)\s*$" % re.escape(key), text, re.M)
    return m.group(1).strip("'\"") if m else None


def set_var(text, key, value):
    line = '%s: "%s"' % (key, value)
    if re.search(r"^%s:" % re.escape(key), text, re.M):
        return re.sub(r"^%s:.*$" % re.escape(key), lambda _: line, text, flags=re.M)
    return text.rstrip("\n") + "\n" + line + "\n"


def set_map(text, key, mapping):
    """Replace (or append) a one-level YAML mapping block."""
    text = re.sub(r"^%s:[ \t]*\n(?:[ \t]+.*\n?)*" % re.escape(key), "", text, flags=re.M)
    return text.rstrip("\n") + "\n%s:\n" % key + "".join('  %s: "%s"\n' % kv for kv in mapping.items())


def write_private(path, text):
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    with os.fdopen(fd, "w") as f:
        f.write(text)
    os.chmod(path, 0o600)


def load_resume():
    return json.load(open(RESUME)) if os.path.exists(RESUME) else {}


def save_resume(state):
    write_private(RESUME, json.dumps(state))


def wait_for_api():
    for _ in range(40):
        st, r = call("GET", "/api/status")
        if st == 200:
            return
        time.sleep(3)
    sys.exit("Infisical at %s is not answering - did the infisical play of site.yml succeed?" % H)


def bootstrap(state):
    email, password = os.environ.get("INFISICAL_ADMIN_EMAIL", ""), os.environ.get("INFISICAL_ADMIN_PASSWORD", "")
    if not email or not password:
        sys.exit("Infisical is not initialized and INFISICAL_ADMIN_EMAIL / INFISICAL_ADMIN_PASSWORD are not set "
                 "(GitHub secrets, docs/13 section 2).")
    st, r = call("POST", "/api/v1/admin/bootstrap", {"email": email, "password": password, "organization": ORG_NAME})
    if st != 200:
        sys.exit("bootstrap failed (HTTP %s): %s" % (st, str(r)[:300]))
    state.update(token=r["identity"]["credentials"]["token"], org_id=r["organization"]["id"])
    save_resume(state)
    print("bootstrapped Infisical: admin %s, organization %r" % (email, ORG_NAME))


def create_project(name, tok):
    r = first_ok([
        ("POST", "/api/v1/projects", {"projectName": name, "type": "secret-manager", "shouldCreateDefaultEnvs": True}, tok),
        ("POST", "/api/v2/workspace", {"projectName": name}, tok),
    ], "create project " + name)
    p = r.get("project") or r.get("workspace") or {}
    if not p.get("id"):
        sys.exit("create project %s: unexpected response %s" % (name, str(r)[:200]))
    return p["id"]


def setup_provisioner(state):
    tok, org = state["token"], state["org_id"]
    pids = state.setdefault("projects", {})
    for name in [MAIN, *TARGETS.values()]:
        if name not in pids:
            pids[name] = create_project(name, tok)
            save_resume(state)
            print("created project %r" % name)
    if "prov_id" not in state:
        r = first_ok([("POST", "/api/v1/identities", {"name": PROV, "organizationId": org, "role": "admin"}, tok)],
                     "create identity " + PROV)
        state["prov_id"] = r["identity"]["id"]
        save_resume(state)
    iid = state["prov_id"]
    ua = {"accessTokenTTL": 86400, "accessTokenMaxTTL": 86400, "accessTokenNumUsesLimit": 0}
    st, _ = call("GET", "/api/v1/auth/universal-auth/identities/%s" % iid, token=tok)
    if st != 200:
        retry(lambda: call("POST", "/api/v1/auth/universal-auth/identities/%s" % iid, ua, tok), "universal-auth for " + PROV)
    state["prov_client_id"] = retry(lambda: call("GET", "/api/v1/auth/universal-auth/identities/%s" % iid, token=tok),
                                    "read client id")["identityUniversalAuth"]["clientId"]
    state["prov_client_secret"] = retry(lambda: call("POST", "/api/v1/auth/universal-auth/identities/%s/client-secrets" % iid,
                                                     {"description": "ansible-managed (ADR 112)"}, tok), "client secret")["clientSecret"]
    save_resume(state)
    role = {"roles": [{"role": "admin", "isTemporary": False}]}
    for name, pid in pids.items():
        first_ok([
            ("POST", "/api/v1/projects/%s/identity-memberships/%s" % (pid, iid), role, tok),
            ("POST", "/api/v2/workspace/%s/identity-memberships/%s" % (pid, iid), role, tok),
            ("POST", "/api/v2/workspace/%s/identity-memberships/%s" % (pid, iid), {"role": "admin"}, tok),
        ], "add %s to %s" % (PROV, name), tolerate=("already", "exist"))
    print("created %s (Admin on %d projects)" % (PROV, len(pids)))


def main():
    wait_for_api()
    st, cfg = call("GET", "/api/v1/admin/config")
    initialized = bool(cfg.get("config", {}).get("initialized", cfg.get("initialized")))

    all_text = open(ALL).read() if os.path.exists(ALL) else "---\n"
    state = load_resume()
    prov_id, prov_secret = read_var(all_text, "infisical_provision_client_id"), read_var(all_text, "infisical_provision_client_secret")
    tok = login(prov_id, prov_secret) if prov_id and prov_secret else None

    if tok is None:
        if not initialized:
            bootstrap(state)
        elif "token" not in state:
            sys.exit("Infisical is already initialized but %s has no working provisioner credentials and there is "
                     "no resume file. Recover through the UI (docs/13 section 2): log in as the org admin, create "
                     "client credentials for %s and put them in all.yml." % (ALL, PROV))
        setup_provisioner(state)
        prov_id, prov_secret = state["prov_client_id"], state["prov_client_secret"]
        tok = None
        for _ in range(10):
            tok = login(prov_id, prov_secret)
            if tok:
                break
            time.sleep(3)
        if not tok:
            sys.exit("the new %s credentials do not log in" % PROV)
    print("logged in as %s" % PROV)

    # Already fully bootstrapped (every routine converge): the agents' credentials still log in.
    def agent_ok(path, idk, sk):
        t = open(path).read() if os.path.exists(path) else ""
        a, b = read_var(t, idk), read_var(t, sk)
        return bool(a and b and login(a, b))
    if (not state and agent_ok(WEB, "infisical_agent_client_id", "infisical_agent_client_secret")
            and agent_ok(CW, "critterwatch_agent_client_id", "critterwatch_agent_client_secret")):
        print("Infisical already bootstrapped - nothing to do")
        return

    org = state.get("org_id")
    if not org:
        st, r = call("GET", "/api/v1/organization", token=tok)
        org = (r.get("organizations") or [{}])[0].get("id")
    st, r = call("GET", "/api/v2/organizations/%s/workspaces" % org, token=tok)
    projects = {w["name"]: w for w in r.get("workspaces", [])}
    for name in [MAIN, *TARGETS.values()]:
        if name not in projects:
            sys.exit("project %r not visible to %s (visible: %s)" % (name, PROV, sorted(projects)))

    new_creds, project_ids = {}, {}
    for target, pname in TARGETS.items():
        pid = projects[pname]["id"]
        project_ids[target] = pid
        st, ws = call("GET", "/api/v1/workspace/%s" % pid, token=tok)
        slugs = [e["slug"] for e in ws.get("workspace", {}).get("environments", [])]
        if ENV not in slugs:
            sys.exit("project %r has no %r environment (has %s)" % (pname, ENV, slugs))

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
                                    {"description": "ansible-managed (ADR 112)"}, tok), "client secret for " + name)["clientSecret"]
        retry(lambda: call("PATCH", "/api/v2/workspace/%s/identity-memberships/%s" % (pid, iid),
                           {"roles": [{"role": "viewer", "isTemporary": False}]}, tok), "set viewer role for " + name)
        new_creds[target] = (client_id, secret)

        # prove the isolation before touching any Ansible file
        t2 = retry(lambda: (0, {}) if not login(client_id, secret) else (200, {"t": login(client_id, secret)}), "login " + name)["t"]
        st, _ = call("GET", "/api/v3/secrets/raw?workspaceId=%s&environment=%s&secretPath=/" % (pid, ENV), token=t2)
        if st != 200:
            sys.exit("%s cannot read its own project (HTTP %s)" % (name, st))
        for other_name in [MAIN, *TARGETS.values()]:
            if other_name == pname:
                continue
            st, _ = call("GET", "/api/v3/secrets/raw?workspaceId=%s&environment=%s&secretPath=/" % (projects[other_name]["id"], ENV), token=t2)
            print("   isolation: %-16s -> %-24s HTTP %s %s" % (name, other_name, st, "(denied, good)" if st in (401, 403, 404) else "<-- NOT denied!"))
            if st == 200:
                sys.exit("isolation check FAILED - %s can read %s" % (name, other_name))

    all_text = set_var(all_text, "infisical_project_id", projects[MAIN]["id"])
    all_text = set_var(all_text, "infisical_environment", ENV)
    all_text = set_var(all_text, "infisical_provision_client_id", prov_id)
    all_text = set_var(all_text, "infisical_provision_client_secret", prov_secret)
    all_text = set_map(all_text, "infisical_project_ids", project_ids)
    write_private(ALL, all_text)
    web_text = open(WEB).read() if os.path.exists(WEB) else "---\n"
    web_text = set_var(set_var(web_text, "infisical_agent_client_id", new_creds["brewhouse"][0]),
                       "infisical_agent_client_secret", new_creds["brewhouse"][1])
    write_private(WEB, web_text)
    write_private(CW, set_var(set_var("---\n", "critterwatch_agent_client_id", new_creds["critterwatch"][0]),
                              "critterwatch_agent_client_secret", new_creds["critterwatch"][1]))
    if os.path.exists(RESUME):
        os.remove(RESUME)
    print("updated %s, %s, %s" % (ALL, WEB, CW))


if __name__ == "__main__":
    main()
