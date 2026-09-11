# SSL/TLS Strategy: Let's Encrypt Wildcard Certificates

> **Scope:** this document covers **production** (VLAN 110 ingress behind Kemp). Non-prod PR preview environments use a separate internal PKI — step-ca issuing per-PR certificates to Caddy over ACME, anchored by a locally-trusted root CA (ADR 20, `docs/11-pr-preview-environments.md`).

To achieve a true "green padlock" (trusted SSL) for internal services without exposing them to the internet, this architecture utilizes the **DNS-01 Challenge** via Cloudflare, fully automated by the Kemp LoadMaster.

## The Architecture
1.  **Kemp LoadMaster** handles SSL Termination for the entire VLAN 110 Ingress tier.
2.  Kemp communicates with the **Let's Encrypt ACME API**.
3.  Let's Encrypt requests domain validation.
4.  Kemp uses your **Cloudflare Global API Key** to temporarily inject a DNS TXT record (`_acme-challenge.smartsoftwarecoffee.com`).
5.  Let's Encrypt verifies the TXT record over the public internet and issues the wildcard certificate to Kemp.
6.  Kemp automatically removes the TXT record and binds the new certificate to your Virtual Services.

## Step-by-Step Implementation in Kemp

1.  **Prepare Cloudflare:** Log into Cloudflare, navigate to **My Profile > API Tokens**, and copy your **Global API Key** (Note: Kemp strictly requires the Global API Key, not a scoped user token).
2.  **Access ACME settings in Kemp:** In the LoadMaster UI, navigate to **Certificates & Security > ACME Certificates**.
3.  **Link Account:** Enter your email address to register or link your Let's Encrypt account.
4.  **Request New Certificate:**
    *   **Certificate Identifier:** `Homelab-Wildcard`
    *   **Common Name:** `*.smartsoftwarecoffee.com` (This wildcard covers your apps and admin panels).
    *   **Select Virtual Service:** Select the `:443` VS at `10.10.110.199` (ADR 23 — not the WUI's `10.10.10.199`; that address is management-only and never carries a Virtual Service). *Note: this doc says the VS must use SubVSs rather than direct Real Servers to validate — unverified against the actual build, which has Real Servers attached directly (`docs/03` section 1). If ACME validation rejects the direct-Real-Servers setup when this step is actually run, restructure into a parent VS + SubVS and update this note with what was actually required.*
    *   **DNS API Provider:** Select `CloudFlare`.
    *   **DNS API Username:** Your Cloudflare account email.
    *   **DNS API Access Key:** Your Cloudflare Global API Key.
5.  **Submit:** Click **Request Certificate**. Kemp will handle the DNS-01 challenge and automatically renew the certificate every 60 days.

---
### Source Material & Attribution
This configuration strictly follows the Progress Kemp Documentation: "Request a Wildcard Certificate" and "Let's Encrypt on LoadMaster".
