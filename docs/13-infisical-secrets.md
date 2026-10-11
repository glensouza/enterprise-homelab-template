# Infisical: secret layout, bootstrap, and rebuild

How secrets are stored and delivered to the apps (docs/01 ADR 12, 107, 108). Read this before
recreating the environment: the Infisical bootstrap has a few steps that is fully scripted (section 2), and the order matters.

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

## 2. Bootstrap: fully scripted (ADR 112)

Nothing is done in the UI. `ansible/files/bootstrap-infisical.py` (run by `ansible/converge.sh`, which both
`terraform.yml` and `ansible-converge.yml` call) does it on the devops runner:

1. If the instance is uninitialized, `POST /api/v1/admin/bootstrap` creates the admin account and the
   `BrewHouse` organization from the GitHub secrets `INFISICAL_ADMIN_EMAIL` / `INFISICAL_ADMIN_PASSWORD`.
2. Creates the projects `BrewHouse`, `BrewHouse Web`, `BrewHouse CritterWatch`, and the org-level identity
   `brewhouse-provisioner` (Admin on all three).
3. Creates a viewer-only identity inside each consumer project (`brewhouse-agent`, `critterwatch-agent`),
   **proves isolation** (each reads its own project and gets 401/403/404 on the others), and writes
   `/opt/ansible-secrets/all.yml` (project ids, provisioner credentials), `web.yml`, `critterwatch.yml` (0600).
   Secrets are never printed.

Set the two GitHub secrets once (the password must satisfy Infisical's strength rules):

```bash
gh secret set INFISICAL_ADMIN_EMAIL --body "you@example.com"
gh secret set INFISICAL_ADMIN_PASSWORD      # prompts; not echoed
```

The script is idempotent: with working credentials it exits at once; the one-shot bootstrap token and ids
are kept in `/opt/ansible-secrets/infisical-bootstrap.json` until the run completes, so a half-finished run
resumes. If Infisical is initialized but the credentials are lost and that file is gone, recover in the UI:
log in as the org admin, create client credentials for `brewhouse-provisioner`, put them in `all.yml`, and
re-run (it then repairs the rest).

---

## 3. Rebuild order (fresh environment)

1. Run the Terraform workflow (`terraform apply`); its chained converge brings Infisical up, the script
   bootstraps it, then every other role pushes its secrets straight into the right project
   (`infisical_secret_target` -> `infisical_project_ids`) and each Agent renders only its own project.
2. Verify:
   - `grep -c = /etc/brewhouse/brewhouse.env` -> `5`; `/etc/critterwatch/critterwatch.env` -> `2`.
   - No admin credential names (`TECHNITIUM_ADMIN_PASSWORD`, `DEVOPS_LXC_ROOT_PASSWORD`, ...) in either file.
   - `systemctl restart blazor-app` / `critterwatch` - a running process keeps its old environment.
3. Manual vault entries (e.g. the devops root password) go in with
   `ansible-playbook store-secret.yml -e @/opt/ansible-secrets/all.yml -e infisical_secret_name=<NAME>`
   - they land in `/admin` by default (`-e store_secret_target=brewhouse|critterwatch` overrides).

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
