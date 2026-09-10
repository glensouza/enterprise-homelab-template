# UniFi Network Segregation & VLAN Isolation

To enforce true zero-trust boundaries in your home lab, we leverage the UniFi UDM-Pro's native VLAN routing and firewall capabilities. This prevents lateral movement in the event a web-facing service is compromised.

To avoid IP addressing conflicts and DHCP exhaustion on the UDM-Pro, **all LXCs must be assigned static IPs outside the primary DHCP range of their respective subnets.**

---

## 1. UniFi Virtual Networks (VLANs)

> **IaC target state (ADR 17):** these networks and the section 3 firewall rules are declared in `terraform/unifi.tf` and applied by Terraform — see `docs/08-infrastructure-as-code.md`. The GUI steps below document the same end state (and serve as the manual fallback). **Keep this document and `terraform/unifi.tf` in sync.**

In the **UniFi Network Application**, navigate to **Settings > Networks** and create the following networks:

1.  **Web / Ingress Tier (VLAN 110)**
    *   **Router:** UDM-Pro
    *   **Host Address:** `10.10.110.1/24`
    *   **Purpose:** Houses the Cloudflare Tunnel LXC and the Blazor Web LXCs. The Kemp VIP
        (`10.10.10.199`) and the Synology NAS (`10.10.10.90`) are pre-existing, non-Terraform-managed
        hardware and stay on the existing `10.10.10.0/24` LAN rather than moving into this VLAN.
2.  **Backend / Data Tier (VLAN 120)**
    *   **Router:** UDM-Pro
    *   **Host Address:** `10.10.120.1/24`
    *   **Purpose:** Houses databases (PostgreSQL), caches/SignalR backplanes (Garnet), and queues (RabbitMQ). Entirely isolated from the internet.
3.  **Management / Infrastructure Tier (VLAN 130)**
    *   **Router:** UDM-Pro
    *   **Host Address:** `10.10.130.1/24`
    *   **Purpose:** Houses infrastructure-tool LXCs (Infisical, Uptime Kuma, Grafana/Alloy/Loki, Technitium DNS, step-ca). The Proxmox hosts' own management IPs are NOT here — pve1/pve3/pve4 are pre-existing hardware on the existing 10.10.10.0/24 LAN, same as Kemp and the NAS.
4.  **Non-Prod / Preview Tier (VLAN 140)**
    *   **Router:** UDM-Pro
    *   **Host Address:** `10.10.140.1/24`
    *   **Purpose:** Houses the single PR preview host (ADR 19, `docs/11`). May only reach Technitium DNS and the step-ca ACME endpoint on VLAN 130 — fully isolated from every production tier.

---

## 2. Proxmox LXC VLAN Tagging

When you provision an LXC using the `community-scripts`, or when editing its network configuration in the Proxmox GUI:
1.  Go to the LXC **Network** tab.
2.  Edit `eth0`.
3.  Set the **VLAN Tag** to `110`, `120`, or `130` matching the matrix in `CLAUDE.md`.
4.  Set a **Static IP** matching the subnet (e.g., `10.10.120.110/24`) and set the **Gateway** to the UniFi router for that VLAN (e.g., `10.10.120.1`).

---

## 3. UniFi Firewall Rules (LAN IN)

To isolate the environments, navigate to **Settings > Security > Firewall Rules** in UniFi and configure these rules under the **LAN IN** tab (Order matters: top to bottom):

| Action | Source | Destination | Ports | Purpose |
| :--- | :--- | :--- | :--- | :--- |
| **Accept** | VLAN 110 (Web) | `10.10.120.110` (Postgres) | `5432` | Allow Blazor apps to query the database. |
| **Accept** | VLAN 110 (Web) | `10.10.120.111` (Garnet) | `6379` | Allow Blazor apps to read/write cache & SignalR backplane. |
| **Accept** | VLAN 110 (Web) | `10.10.120.112` (RabbitMQ) | `5672` | Allow Blazor apps to publish messages. |
| **Accept** | VLAN 130 (Management)| `10.10.10.90` (Synology NAS)| `Any` | Allow the VLAN 130 admin/monitoring LXCs to reach the NAS. |
| **Accept** | `10.10.10.101` (pve1), `10.10.10.102` (pve2), `10.10.10.103` (pve3), `10.10.10.104` (pve4) | `10.10.10.90` (Synology NAS) | `Any` | Allow all four (pre-existing, non-VLAN-130) Proxmox cluster members to reach shared NFS storage — Proxmox mounts cluster-wide storage on every node regardless of which two actually host LXCs. One rule per host — they're on the existing LAN, not a UniFi network Terraform can reference as a group. |
| **Accept** | VLAN 120 (Data Tier)| `10.10.10.90` (Synology NAS)| `2049, 111` | Allow Postgres to write to NFS mounts. |
| **Drop** | VLAN 110 (Web) | VLAN 120 (Data Tier) | `Any` | Block all other Web -> Backend traffic. |
| **Drop** | VLAN 110 (Web) | VLAN 130 (Management) | `Any` | Block Web -> Proxmox GUI / Management. |
| **Accept** | VLAN 130 (Management)| `Any` | `Any` | Allow administrative/monitoring tools full access. |
| **Accept** | VLAN 140 (Preview) | `10.10.130.119` (Technitium) | `53` | Allow preview host to resolve `*.pr.brewhouse.internal`. |
| **Accept** | VLAN 140 (Preview) | `10.10.130.121` (step-ca) | `4443` | Allow Caddy to reach the ACME directory. |
| **Accept** | `10.10.130.121` (step-ca) | VLAN 140 (Preview) | `80, 443` | Allow the CA to complete ACME HTTP-01/TLS-ALPN-01 validation. |
| **Accept** | `10.10.140.120` (Preview host) | `10.10.120.110` (Postgres) | `5432` | pgAdmin (admin tooling, ADR 21) -> production database. |
| **Accept** | `10.10.140.120` (Preview host) | `10.10.120.111` (Garnet) | `6379` | RedisInsight (admin tooling, ADR 21) -> production cache. |
| **Drop** | VLAN 140 (Preview) | VLAN 110 (Web) | `Any` | Isolate non-prod from the web tier. |
| **Drop** | VLAN 140 (Preview) | VLAN 120 (Data Tier) | `Any` | Isolate non-prod from production data (all other). |
| **Drop** | VLAN 140 (Preview) | VLAN 130 (Management) | `Any` | Block all other Preview -> Management traffic. |

*Note: access from the admin LAN to the preview host (HTTPS 443, and SSH from the self-hosted runner) is allowed by the UDM-Pro's default inter-VLAN permit; only VLAN-to-VLAN isolation is locked down above.*
