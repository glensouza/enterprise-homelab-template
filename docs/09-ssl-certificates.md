# SSL/TLS Strategy: Let's Encrypt Wildcard Certificates

> **Scope:** this document covers **production** (VLAN 110 ingress behind Kemp). Non-prod PR preview environments use a separate internal PKI — step-ca issuing per-PR certificates to Caddy over ACME, anchored by a locally-trusted root CA (ADR 20, `docs/11-pr-preview-environments.md`).

To achieve a true "green padlock" (trusted SSL) for internal services without exposing them to the internet, this architecture utilizes the **DNS-01 Challenge** via Cloudflare, fully automated by the Kemp LoadMaster.

## The Architecture
1.  **Kemp LoadMaster** handles SSL Termination for the entire VLAN 110 Ingress tier.
2.  Kemp communicates with the **Let's Encrypt ACME API**.
3.  Let's Encrypt requests domain validation.
4.  Kemp uses your **Cloudflare Global API Key** to temporarily inject a DNS TXT record (`_acme-challenge.smartsoftwarecoffee.com`).
5.  Let's Encrypt verifies the TXT record over the public internet and issues the wildcard certificate to Kemp.
6.  Kemp removes the TXT record automatically. **It does not auto-bind the certificate to any Virtual Service** — confirmed live, that's a separate manual step (section 3 below).

## 1. Prerequisite: the target VS must use a SubVS, not direct Real Servers

Confirmed live: Kemp's ACME "Select a VS" dropdown is empty for any Virtual Service with Real Servers attached directly — it only lists VSs built as a Content-Switching parent with a **SubVS**. If `Blazor-App-VIP` (`10.10.110.199:443`, ADR 23) isn't already structured this way, convert it first (official Kemp docs: [Convert a Virtual Service with Real Servers to one with SubVSs](https://docs.progress.com/bundle/loadmaster-feature-description-lets-encrypt-ga/page/Convert-a-Virtual-Service-with-Real-Servers-to-one-with-SubVSs.html)):

1. **Virtual Services → View/Modify Services → Modify** the `:443` VS.
2. Expand **Real Servers** and delete both real servers attached directly to the parent (note their IP:port first — `10.10.110.101:5000` / `10.10.110.102:5000`).
3. The Real Servers section now shows an **Add SubVS ...** button instead (it only appears once the parent has zero direct real servers). Click it — Kemp creates a SubVS automatically.
4. **Modify** the new SubVS and configure its own **Real Servers** section:
   * `Real Server Check Method`: `HTTP Protocol` (defaults to HTTPS — same gotcha as `docs/03` section 1, since the backend never sees TLS).
   * `URL`: `/health`, `HTTP Method`: `GET`.
   * **Add New** the two real servers. **Gotcha, confirmed live: the "Add a Real Server" form defaults Port to `80`, not `5000`** — this is easy to miss since the same "Add New" flow may pop a native browser prompt for just the IP address (see note below), silently leaving the port at its default. After adding both, open **Modify** on each one and correct the port to `5000` if it's wrong.
5. Content Switching on the parent VS does **not** need to be manually enabled — creating a SubVS handles the routing; the parent's Advanced Properties will still show "Disabled" and that's fine.

> **Native dialog note:** on some LoadMaster builds, "Add New" under a SubVS's Real Servers opens a plain browser `prompt()` for the IP address instead of a full form page — if so, add the IP there, then immediately open **Modify** on the resulting entry to fix the port (defaults to 80) before moving on.

## 2. Request the certificate

1.  **Prepare Cloudflare:** Log into Cloudflare, navigate to **My Profile > API Tokens**, and copy your **Global API Key** (Note: Kemp strictly requires the Global API Key, not a scoped user token).
2.  **Access ACME settings in Kemp:** In the LoadMaster UI, navigate to **Certificates & Security > ACME Certificates > Let's Encrypt**.
3.  **Link Account** (one-time): enter your email address to register your Let's Encrypt account. Confirmed live: this step succeeds independently of the certificate request below — Kemp's audit log records "Let's Encrypt ACME Account Successfully Registered" as its own event, so if the *next* step fails, the account itself is still fine and doesn't need re-linking.
4.  **Request New Certificate:**
    *   **Certificate Identifier:** `Homelab-Wildcard`
    *   **Common Name:** `*.smartsoftwarecoffee.com` (this wildcard covers your apps and admin panels)
    *   **Select a VS** (the dropdown next to Common Name — not the second one next to SAN/UCC Names, which is only for adding extra alternate names): `10.10.110.199:443` (ADR 23 — not the WUI's `10.10.10.199`, which never carries a Virtual Service and won't appear here). This dropdown is empty until section 1 above is done.
    *   Once a VS is selected *and* the Common Name field has been typed into, a **DNS API Provider** dropdown appears next to it — select `CloudFlare`, which reveals **DNS API Username** (your Cloudflare account email) and **DNS API Access Key** (your Cloudflare Global API Key) fields.
5.  **Submit:** Click **Request Certificate**. A "Signing a Certificate with CA. Please Wait..." modal appears — confirmed live, this took roughly 20-30 seconds (Cloudflare DNS propagation + Let's Encrypt's DNS-01 validation) before returning to the certificate list with the new entry, showing its expiry date (~90 days out) and the VS it validated against.
6.  If the certificate list stays empty and no error appears, the request likely never actually reached the server — confirmed live this can happen with no visible error. Check **System Configuration → Logging Options → System Log Files → Audit LogFile**: a successful request logs distinct account-registration and certificate events; if there's nothing after the account registration line, re-submit the form (all fields, including the DNS API credentials, need to be re-entered — Kemp doesn't persist them across a page navigation).

## 3. Bind the certificate to the Virtual Service

**This is a separate, easy-to-miss manual step** — issuing the certificate does not attach it anywhere on its own:

1. **Certificates & Security → SSL Certificates.** The new cert appears here as soon as it's issued, but its "Virtual Services" column is blank and the VS still shows under **Self Signed Virtual Services** at the bottom of the page.
2. In the certificate's row, move `10.10.110.199:443` from **Available VSs** to **Assigned VSs** (select it, click `>`), then click **Save Changes**.
3. Confirm: the "Self Signed Virtual Services" list should no longer include `10.10.110.199:443`, and the cert's row should now show it under "Virtual Services".

Auto-renewal: **Certificates & Security → ACME Certificates → Let's Encrypt**, `Renew Period` field (default `30`, valid range 1-60 days before expiry) — confirmed live as 30, not the 60 previously assumed here.

---
### Source Material & Attribution
This configuration strictly follows the Progress Kemp Documentation: "Request a Wildcard Certificate" and "Let's Encrypt on LoadMaster", plus [Convert a Virtual Service with Real Servers to one with SubVSs](https://docs.progress.com/bundle/loadmaster-feature-description-lets-encrypt-ga/page/Convert-a-Virtual-Service-with-Real-Servers-to-one-with-SubVSs.html) for the SubVS prerequisite.
