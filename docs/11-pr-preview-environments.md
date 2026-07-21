# PR Preview Environments (Non-Prod)

Every open pull request against `main` gets its own isolated, fully-integrated environment — app + PostgreSQL (pgvector) + Garnet + RabbitMQ — reachable over trusted local HTTPS at **`https://pr-<number>.pr.roadrunner.internal`**. When the PR is merged or closed, the environment is destroyed automatically. Nothing is ever publicly exposed. See ADR 19 (preview model) and ADR 20 (internal DNS + PKI).

```text
Developer LAN                    VLAN 30 (Management)                 VLAN 40 (Non-Prod)
┌──────────────┐   DNS: *.pr.roadrunner.internal ──▶ 10.10.40.120
│   Browser    │        ┌────────────────────┐      ┌────────────────────────────────────┐
│ (trusts step │───────▶│ Technitium DNS     │      │ pr-preview LXC (10.10.40.120)      │
│  -ca root)   │        │ 10.10.30.119       │      │  Caddy :443 ──TLS── step-ca (ACME) │
└──────┬───────┘        └────────────────────┘      │    │                                 │
       │ HTTPS pr-42.pr.roadrunner.internal         │    ├─▶ 127.0.0.1:6042  pr-42/app    │
       │            trust chain                     │    │     ├─ db (pgvector) :15442*   │
       └───────────────────────────────────────────▶│    │     ├─ cache (Garnet)          │
                          ┌────────────────────┐    │    │     └─ messaging (RabbitMQ)    │
                          │ step-ca            │◀───┼────┤  ACME issuance per PR site     │
                          │ 10.10.30.121:4443  │    │    ├─▶ 127.0.0.1:6017  pr-17/...    │
                          └────────────────────┘    │    └─▶ ... one stack per open PR    │
 GitHub Actions (self-hosted runner) ──SSH─────────▶│        *db published on loopback   │
   build image → docker load → compose up →         │         only, for the EF bundle    │
   EF bundle → Caddy site → comment PR URL          └────────────────────────────────────┘
```

---

## 1. Architecture

| Piece | Where | Role |
| :--- | :--- | :--- |
| **Preview host** | VLAN 40, `10.10.40.120` (`pr-preview` LXC) | Runs Docker. One compose stack per open PR under `/opt/previews/pr-<n>/` (ADR 19). |
| **Caddy** | preview host | TLS termination + reverse proxy. One site file per PR in `/etc/caddy/pr-sites/pr-<n>.caddy`, proxying to the stack's loopback app port (`6000 + <PR#>`). |
| **Technitium DNS** | VLAN 30, `10.10.30.119` | Private zone `pr.roadrunner.internal` with a single wildcard A record `* → 10.10.40.120` (ADR 20). No per-PR DNS records, ever. |
| **step-ca** | VLAN 30, `10.10.30.121:4443` | Internal CA with an ACME provisioner. Caddy auto-issues/renews a certificate per PR hostname. |
| **CI** | `.github/workflows/pr-preview.yml` / `pr-preview-cleanup.yml` | Deploy on PR open/sync, teardown on PR close. |

Why a wildcard record instead of per-PR DNS entries: there is nothing to create on PR open and nothing to forget on merge — the entire DNS lifecycle for previews is one static record. The `.internal` TLD is ICANN-reserved for private use, so the zone can never collide with a public name.

> **Docker only here.** ADR 02 (bare-metal systemd, no Docker) still governs production. Docker is used on the preview host because compose gives perfect per-PR isolation and atomic cleanup (`down -v` removes containers, networks, and the PR database volume).

## 2. Prerequisites (one-time)

1. **Terraform:** `terraform apply` creates VLAN 40 + firewall rules and the three LXCs (`pr-preview`, `technitium-dns`, `step-ca`) — see `terraform/lxc.tf` / `unifi.tf` and `docs/08`.
2. **Ansible:** `ansible-playbook site.yml` converges the new hosts:
   * `dns` → `technitium` role (installs the server; zone/record via API if `technitium_api_token` is set in `ansible/inventory/group_vars/dns.yml`)
   * `pki` → `resolver` + `step-ca` roles (initializes the CA with an ACME provisioner, fetches `root_ca.crt` to `ansible/fetched/step-ca/`)
   * `preview` → `resolver` + `docker` + `preview-host` roles (Docker Engine, Caddy wired to the step-ca ACME directory)
3. **Technitium:** browse `http://10.10.30.119:5380`, change the default `admin` password, and either set `technitium_api_token` in `group_vars/dns.yml` and re-run the playbook, or manually create primary zone `pr.roadrunner.internal` with an A record `*` → `10.10.40.120`.
4. **Client DNS:** devices that browse previews must resolve via Technitium — set `10.10.30.119` as the DNS server on the admin LAN's DHCP scope (or per-device).
5. **GitHub:** create a `preview` environment (no required reviewers needed). The self-hosted runner needs Docker CLI, SSH access to `10.10.40.120` / `10.10.30.121`, and `openssl`.

## 3. Lifecycle of a PR environment

**Open / push (`pr-preview.yml`, runs on the self-hosted runner, `preview` environment):**

1. `dotnet test -c Release` — tests gate the preview, same as production.
2. Builds `roadrunner-pr-<n>:<sha>` from `src/RoadrunnerAuction/Dockerfile`, `docker save | ssh … docker load`.
3. Generates the EF Core migration bundle (ADR 11) and stages `/opt/previews/pr-<n>/` with `docker-compose.yml` (from `deploy/preview/docker-compose.pr.yml`) and a `.env` containing an ephemeral per-PR database password — no GitHub secrets required.
4. `docker compose up -d --wait`, then executes the migration bundle against the PR database (`roadrunner_pr<n>` on the loopback-published port `15432 + <n>`).
5. Writes the Caddy site `pr-<n>.pr.roadrunner.internal → 127.0.0.1:<6000+n>` and reloads Caddy. The first TLS handshake triggers ACME issuance from step-ca.
6. Smoke-tests `https://pr-<n>.pr.roadrunner.internal/health` with the real certificate chain (`--cacert root_ca.crt`) and comments the URL on the PR.

**Merge / close (`pr-preview-cleanup.yml`):**

* `docker compose down -v` (removes containers, networks, and volumes — including the PR database), deletes `/opt/previews/pr-<n>/` and the Caddy site file, reloads Caddy, prunes dangling images, and comments the teardown on the PR. DNS needs no cleanup (wildcard).

## 4. Trusting the internal CA (one-time per client)

Browsers show the green padlock only after the step-ca root certificate is trusted. Get it from `ansible/fetched/step-ca/root_ca.crt` (fetched by the playbook) or directly:

```bash
scp root@10.10.30.121:/root/.step/certs/root_ca.crt .
```

* **Windows (current user):** `certutil -addstore -user Root root_ca.crt`
* **Windows (all users, admin):** `certutil -addstore Root root_ca.crt`
* **Domain fleet:** distribute via GPO — *Computer Configuration → Policies → Windows Settings → Security Settings → Public Key Policies → Trusted Root Certification Authorities*.

## 5. Operations & troubleshooting

| Task | Command / location |
| :--- | :--- |
| List running preview stacks | `ssh root@10.10.40.120 "docker compose ls"` |
| Logs for one PR | `ssh root@10.10.40.120 "cd /opt/previews/pr-<n> && docker compose logs -f app"` |
| List Caddy sites | `ls /etc/caddy/pr-sites/` on the preview host |
| Force re-issue a cert | delete the site file, `systemctl reload caddy`, restore file, reload again |
| CA status / ACME directory | `curl --cacert root_ca.crt https://10.10.30.121:4443/acme/acme/directory` |
| Manually remove a stale PR | run the steps from `pr-preview-cleanup.yml` by hand |

* **`NET::ERR_CERT_AUTHORITY_INVALID`** — the client doesn't trust the step-ca root (section 4).
* **Hostname doesn't resolve** — the client isn't using Technitium for DNS (section 2, step 4).
* **Caddy can't obtain a certificate** — check VLAN 40 → `10.10.30.121:4443` and step-ca → preview `80,443` firewall rules (`terraform/unifi.tf`, ADR 19), and that the step-ca LXC resolves `*.pr.roadrunner.internal` via Technitium (the `resolver` role).
* **Port collisions** — app/DB ports are `6000 + <PR#>` / `15432 + <PR#>`; GitHub PR numbers are unique, so collisions are impossible in practice.

## 6. Deliberate simplifications vs. production

| Production (VLANs 10/20/30) | Preview (VLAN 40) |
| :--- | :--- |
| Bare-metal systemd (ADR 02) | Docker compose stacks (ADR 19) |
| HA: 2 web nodes + Kemp VIP + sticky sessions | Single Caddy instance |
| Infisical Agent secrets (ADR 12) | Ephemeral per-PR DB password generated in CI, stored only in the stack's `.env` |
| pgBackRest PITR + pre-migration `pg_dump` (ADR 16/18) | No backups — environments are disposable |
| Let's Encrypt DNS-01 wildcard on Kemp (ADR 13) | step-ca ACME per-host certs (ADR 20) |
| OTLP → Grafana Alloy (ADR 09) | No telemetry by default (set `OTEL_EXPORTER_OTLP_ENDPOINT` in the stack `.env` if wanted) |

---
### Source Material & Attribution
Layout follows smallstep (`step ca init --acme`), Caddy (`acme_ca` / `acme_ca_root`), Technitium DNS (API `/api/zones/...`), and Docker Compose (`--env-file`, `down -v`) official documentation.
