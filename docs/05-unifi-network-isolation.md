# UniFi Network Segregation & VLAN Isolation

To enforce true zero-trust boundaries in your home lab, we leverage the UniFi UDM-Pro's native VLAN routing and firewall capabilities. This prevents lateral movement in the event a web-facing service is compromised.

To avoid IP addressing conflicts and DHCP exhaustion on the UDM-Pro, **all LXCs must be assigned static IPs outside the primary DHCP range of their respective subnets.**

---

## 1. UniFi Virtual Networks (VLANs)

> **IaC target state (ADR 17):** these networks and the section 3 firewall rules are declared in `terraform/unifi.tf` and applied by Terraform — see `docs/08-infrastructure-as-code.md`. The GUI steps below document the same end state (and serve as the manual fallback). **Keep this document and `terraform/unifi.tf` in sync.**

In the **UniFi Network Application**, navigate to **Settings > Networks** and create the following networks:

1.  **Web / Ingress Tier (VLAN 10)**
    *   **Router:** UDM-Pro
    *   **Host Address:** `10.10.10.1/24`
    *   **Purpose:** Houses the Cloudflare Tunnel, the Kemp VIP (`10.10.10.199`), the Synology NAS (`10.10.10.90`), and the Blazor Web LXCs.
2.  **Backend / Data Tier (VLAN 20)**
    *   **Router:** UDM-Pro
    *   **Host Address:** `10.10.20.1/24`
    *   **Purpose:** Houses databases (PostgreSQL), caches/SignalR backplanes (Garnet), and queues (RabbitMQ). Entirely isolated from the internet.
3.  **Management / Infrastructure Tier (VLAN 30)**
    *   **Router:** UDM-Pro
    *   **Host Address:** `10.10.30.1/24`
    *   **Purpose:** Houses Proxmox host IPs and infrastructure tools (Infisical, Uptime Kuma, Grafana/Alloy/Loki, Technitium DNS, step-ca).
4.  **Non-Prod / Preview Tier (VLAN 40)**
    *   **Router:** UDM-Pro
    *   **Host Address:** `10.10.40.1/24`
    *   **Purpose:** Houses the single PR preview host (ADR 19, `docs/11`). May only reach Technitium DNS and the step-ca ACME endpoint on VLAN 30 — fully isolated from every production tier.

---

## 2. Proxmox LXC VLAN Tagging

When you provision an LXC using the `community-scripts`, or when editing its network configuration in the Proxmox GUI:
1.  Go to the LXC **Network** tab.
2.  Edit `eth0`.
3.  Set the **VLAN Tag** to `10`, `20`, or `30` matching the matrix in `CLAUDE.md`.
4.  Set a **Static IP** matching the subnet (e.g., `10.10.20.110/24`) and set the **Gateway** to the UniFi router for that VLAN (e.g., `10.10.20.1`).

---

## 3. UniFi Firewall Rules (LAN IN)

To isolate the environments, navigate to **Settings > Security > Firewall Rules** in UniFi and configure these rules under the **LAN IN** tab (Order matters: top to bottom):

| Action | Source | Destination | Ports | Purpose |
| :--- | :--- | :--- | :--- | :--- |
| **Accept** | VLAN 10 (Web) | `10.10.20.110` (Postgres) | `5432` | Allow Blazor apps to query the database. |
| **Accept** | VLAN 10 (Web) | `10.10.20.111` (Garnet) | `6379` | Allow Blazor apps to read/write cache & SignalR backplane. |
| **Accept** | VLAN 10 (Web) | `10.10.20.112` (RabbitMQ) | `5672` | Allow Blazor apps to publish messages. |
| **Accept** | VLAN 30 (Management)| `10.10.10.90` (Synology NAS)| `Any` | Allow Proxmox nodes to write backups to the NAS. |
| **Accept** | VLAN 20 (Data Tier)| `10.10.10.90` (Synology NAS)| `2049, 111` | Allow Postgres to write to NFS mounts. |
| **Drop** | VLAN 10 (Web) | VLAN 20 (Data Tier) | `Any` | Block all other Web -> Backend traffic. |
| **Drop** | VLAN 10 (Web) | VLAN 30 (Management) | `Any` | Block Web -> Proxmox GUI / Management. |
| **Accept** | VLAN 30 (Management)| `Any` | `Any` | Allow administrative/monitoring tools full access. |
| **Accept** | VLAN 40 (Preview) | `10.10.30.119` (Technitium) | `53` | Allow preview host to resolve `*.pr.roadrunner.internal`. |
| **Accept** | VLAN 40 (Preview) | `10.10.30.121` (step-ca) | `4443` | Allow Caddy to reach the ACME directory. |
| **Accept** | `10.10.30.121` (step-ca) | VLAN 40 (Preview) | `80, 443` | Allow the CA to complete ACME HTTP-01/TLS-ALPN-01 validation. |
| **Drop** | VLAN 40 (Preview) | VLAN 10 (Web) | `Any` | Isolate non-prod from the web tier. |
| **Drop** | VLAN 40 (Preview) | VLAN 20 (Data Tier) | `Any` | Isolate non-prod from production data. |
| **Drop** | VLAN 40 (Preview) | VLAN 30 (Management) | `Any` | Block all other Preview -> Management traffic. |

*Note: access from the admin LAN to the preview host (HTTPS 443, and SSH from the self-hosted runner) is allowed by the UDM-Pro's default inter-VLAN permit; only VLAN-to-VLAN isolation is locked down above.*
