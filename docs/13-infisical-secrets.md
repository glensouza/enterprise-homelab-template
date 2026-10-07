# Infisical: secret layout, bootstrap, and rebuild

How secrets are stored and delivered to the apps (docs/01 ADR 12, 107, 108). Read this before
recreating the environment: the Infisical bootstrap has a few steps that **only an org admin can do
in the UI**, and the order matters.

---

## 1. Layout: one Infisical project per consumer

| Infisical project | Holds | Read by (identity, role) |
|---|---|---|
| **BrewHouse** (main, `infisical_project_id`) | `/admin` — human-only credentials (Technitium, Homepage, pgAdmin, Portainer passwords, devops root password). Also the provisioning identity's home. | nobody automated. Humans only. |
| **BrewHouse Web** | `ConnectionStrings__brewhousedb`, `__cache`, `__messaging`, `OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_EXPORTER_OTLP_PROTOCOL` (at `/`) | `brewhouse-agent` — **Viewer** of this project only. Renders `/etc/brewhouse/brewhouse.env` on both web nodes. |
| **BrewHouse CritterWatch** | `ConnectionStrings__critterwatch`, `__messaging` (at `/`) | `critterwatch-agent` — **Viewer** of this project only. Renders `/etc/critterwatch/critterwatch.env`. |

Machine identities:

| Identity | Scope | Purpose |
|---|---|---|
| `brewhouse-provisioner` | org-level; **Admin** on all three projects | Ansible: pushes secrets (`roles/infisical-secret`), creates folders. Credentials in `/opt/ansible-secrets/all.yml`. |
| `brewhouse-agent` | project-scoped to BrewHouse Web, Viewer | the Agent on the web nodes. Credentials in `/opt/ansible-secrets/web.yml`. |
| `critterwatch-agent` | project-scoped to BrewHouse CritterWatch, Viewer | the Agent on the CritterWatch LXC. Credentials in `/opt/ansible-secrets/critterwatch.yml`. |

The rule behind it: **never push a human-only credential to a place an Agent reads.** The Agent
renders *everything* it can see into an `EnvironmentFile=`, so every app process inherits it.
(Before ADR 108 the Agent rendered the whole root of one project: the internet-facing web tier ran
with the devops root password in its environment.)

### Why separate *projects* and not folders

Found the hard way on the free plan (Infisical 0.165):

- **Custom roles (path-scoped read) are Enterprise** — `Failed to create custom role due to plan RBAC
  restriction`. Built-in roles (admin/member/viewer/no-access) apply to a whole project, so a folder
  cannot be locked to one identity.
- **Identity IP allow-listing is paid** — `Failed to add IP access range ... due to plan restriction`.
- A project is the smallest unit a built-in role can scope to, so the boundary is a project.
- If the plan is ever upgraded, folders + path-scoped roles would also work; the Ansible roles
  already fall back to a `/<target>` folder in the main project whenever a consumer has no entry in
  `infisical_project_ids`, so both layouts run from the same code.

How Ansible decides where things go (`roles/infisical-secret/defaults/main.yml`,
`roles/infisical-agent/defaults/main.yml`):

- Each push names a **target** (`brewhouse`, `critterwatch`, `admin`).
- If `infisical_project_ids.<target>` is set → that project, path `/`.
- Otherwise → the main project, folder `/<target>` (created on demand).
- `admin` always lives in the main project under `/admin`.

---

## 2. Bootstrap: the steps only an org admin can do (UI)

The provisioning identity cannot create org-level identities or projects (`403`), and a
*project-scoped* identity (like the old `HomeLab Provisioner`) cannot join other projects, so these
are done by hand once, logged in as the org admin at `http://10.10.130.116:8080`:

1. **Create the projects** (Secrets Management → New Project): `BrewHouse` (if not already there),
   `BrewHouse Web`, `BrewHouse CritterWatch`. Note each project's id (the UUID in its URL).
2. **Create the org-level machine identity** `brewhouse-provisioner`
   (Organization → Access Control → Machine Identities → Create). Universal Auth is attached
   automatically. Add it to **all three projects as Admin**
   (identity page → Projects → Add to Project).
3. **Create a client secret** for it (identity page → Universal Auth → View → Add Client Secret).
   It is shown once; copy it. Keep the identity's **Client ID** too.
4. Put the project values in `/opt/ansible-secrets/all.yml` on the devops LXC (mode 0600):

   ```yaml
   infisical_project_id: "<BrewHouse main project id>"
   infisical_environment: "dev"
   infisical_provision_client_id: "<brewhouse-provisioner client id>"
   infisical_provision_client_secret: "<its client secret>"
   infisical_project_ids:
     brewhouse: "<BrewHouse Web project id>"
     critterwatch: "<BrewHouse CritterWatch project id>"
   ```

   (The finisher script in step 3 below writes the provisioner and `infisical_project_ids` values for
   you if you only give it the client id/secret. Do it by hand only if you want to.)

---

## 3. Create the runtime identities + copy secrets: `finish-infisical-split.py`

`ansible/files/finish-infisical-split.py` automates everything after the UI bootstrap, idempotently.
Copy it to the devops LXC and run it **in a real terminal** (it asks for the secret with hidden input):

```bash
scp ansible/files/finish-infisical-split.py devops:/root/
ssh -t devops python3 /root/finish-infisical-split.py <brewhouse-provisioner-client-id>
```

It will:

1. log in as `brewhouse-provisioner`;
2. in each dedicated project, create a viewer-only identity (`brewhouse-agent`, `critterwatch-agent`),
   attach Universal Auth, create its client secret;
3. copy that consumer's secrets from the main project's `/brewhouse` / `/critterwatch` folders into
   the dedicated project (on a **fresh rebuild there is nothing to copy** — it says so and carries on;
   the converge pushes the secrets later);
4. **prove isolation** before changing anything: each new identity must read its own project and get
   `401/403/404` on the others;
5. update the Ansible secret files, keeping the originals as `*.bak-adr108` (0600):
   `all.yml` (provisioner credentials + `infisical_project_ids`), `web.yml` (`infisical_agent_client_id/secret`),
   and a new `critterwatch.yml` (`critterwatch_agent_client_id/secret`). Secrets are never printed.

---

## 4. Rebuild order (fresh environment)

1. Bring up the Infisical LXC (`terraform apply` + the `infisical` Ansible role), create the org
   admin account, then do **section 2** in the UI.
2. Run **section 3** (`finish-infisical-split.py`). Nothing is copied on a fresh install; identities
   and credential files are created.
3. Run the **Ansible Converge** workflow. Roles push every secret straight into the right project
   (`infisical_secret_target` → `infisical_project_ids`), then each Agent renders only its own project.
   `ansible-converge.yml` / `terraform.yml` pass `critterwatch.yml` automatically when it exists.
4. Verify:
   - `grep -c = /etc/brewhouse/brewhouse.env` → `5`; `/etc/critterwatch/critterwatch.env` → `2`.
   - No admin credential names (`TECHNITIUM_ADMIN_PASSWORD`, `DEVOPS_LXC_ROOT_PASSWORD`, …) in either file.
   - `systemctl restart blazor-app` / `critterwatch` — a running process keeps its old environment.
5. Manual vault entries (e.g. the devops root password) go in with
   `ansible-playbook store-secret.yml -e @/opt/ansible-secrets/all.yml -e infisical_secret_name=<NAME>`
   — they land in `/admin` by default (`-e store_secret_target=brewhouse|critterwatch` overrides).

### Migrating an existing environment (what was done 2026-10-06)

The environment started with every secret at the root of the main project and one shared read
identity (`HomeLab`). After running sections 2–3 and a converge:

- delete the old root-level copies of the 11 secrets in the main project (UI or API);
- delete the stray project-scoped identities `brewhouse-agent` / `critterwatch-agent` that were
  created in the **main** project during the first attempt (their credentials were never stored);
- retire the org identity `HomeLab` and the project-scoped `HomeLab Provisioner` (their credentials
  are superseded; `web.yml.bak-adr108` / `all.yml.bak-adr108` hold the old values until you delete them).

---

## 5. Gotchas that cost time

- The roles API (`/api/v1/workspace/<slug>/roles`) takes the project **slug**, not its id.
- A freshly created identity may 404 on the next call for a moment — scripts retry.
- Infisical shows a client secret **once**; if the dialog is closed, create another.
- Any process started before an env-file change keeps the old environment until it is restarted.
- Harness note for AI sessions: remote writes and moving credentials between hosts need explicit
  user approval; leave the secret hand-off to the human (hidden-input prompt), never a clipboard
  pipe or a network listener.
- **Delete protection is ON for every new identity.** The API answers `500` (and a `400` on the PATCH) until
  it is switched off in the UI (identity page → Edit → Delete Protection). Project-scoped identities
  (`Managed by: Project`) can only be deleted from the UI — the `DELETE /api/v1/identities/<id>` route
  `500`s for them; org-level identities delete fine once unprotected. Retiring the pre-split identities
  (`HomeLab`, `HomeLab Provisioner`, the stray `brewhouse-agent`/`critterwatch-agent` in the main project)
  was done this way on 2026-10-06; the type-to-confirm word is `confirm`.
