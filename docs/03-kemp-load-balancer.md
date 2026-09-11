# Kemp LoadMaster Layer 7 Load Balancing & High Availability

To achieve high availability, zero-downtime rolling deployments, and stable WebSocket connections for Blazor Server, web traffic is routed through a Kemp LoadMaster appliance.

Kemp is a VM (`VMID 199` on `pve1`) with **two NICs**, split by function (confirmed live while standing this up — see "Dual-NIC topology" below):

- **`eth0`** — the existing LAN (`10.10.10.0/24`, untagged `vmbr0`). Carries the WUI (management console) only, at `10.10.10.199`.
- **`eth1`** — tagged into VLAN 110 (`vmbr0` with `tag=110` at the Proxmox level, confirmed via `qm config 199`). Carries the actual ingress VIP, alongside the Blazor real servers it load-balances.

---

## Architecture Flow

```text
[ Internet ]
     │
[ Cloudflare Tunnel LXC: 10.10.110.5 ]
     │  (Routes to Kemp VIP, same VLAN 110 as the tunnel and the real servers)
[ Kemp Virtual Service VIP: 10.10.110.199 ] (Source IP Persistence)
     ├───> Real Server 1: Blazor LXC 01 (10.10.110.101:5000)
     └───> Real Server 2: Blazor LXC 02 (10.10.110.102:5000)

[ Kemp WUI: 10.10.10.199 ] — existing LAN, eth0 — management only, never in the traffic path
```

---

## 0. Dual-NIC topology (one-time, per Kemp VM lifetime)

**Why:** Kemp refuses to let a Virtual Service reuse the IP address of *any* of its own interfaces ("Cannot use WUI address as a VIP") — confirmed live, and the rule applies per-interface, not just to eth0/the WUI. Keeping the WUI at `10.10.10.199` (it's also the VM's Proxmox VMID, easy to remember, likely already bookmarked) while wanting the VIP to *also* be `.199` for the same reason meant the two addresses needed separate interfaces/subnets.

1. **Proxmox side** (one-time, done via SSH, not Terraform — Kemp itself is a pre-existing, non-Terraform-managed VM per ADR 22's boundary):
   ```bash
   qm set 199 -net1 virtio=<mac>,bridge=vmbr0,tag=110,firewall=1
   ```
   This is `net1` (the VM's second NIC) tagged into VLAN 110. No VM reboot needed — Proxmox NIC retags apply live.
2. **Kemp WUI side** — **System Configuration → Network Setup → Interfaces → eth1** → static IP. Confirmed live: the interface's own address must be **`/24`** (`10.10.110.0/24`, matching `terraform/unifi.tf`'s `vlan110_cidr`) — a `/8` here makes Kemp treat the *entire* `10.0.0.0/8` range (including eth0's own subnet and every other VLAN) as directly reachable off eth1, which breaks routing between the two interfaces.
3. **eth1's own address must differ from the VIP** (same "no interface address as a VIP" rule as step 1's original eth0 problem) — use `10.10.110.198` for eth1 itself, leaving `10.10.110.199` free for the Virtual Service.
4. Leave **VLAN Configuration**, **VXLAN Configuration**, and **Interface Bonding** (also on the Interfaces page) untouched — Proxmox already delivers eth1 pre-tagged/untagged-from-the-guest's-perspective, so configuring a VLAN ID again inside Kemp would double-tag; VXLAN and bonding don't apply to this setup at all.

---

## 1. Kemp Virtual Service Setup

1. Log into the Kemp LoadMaster Web Console (`https://10.10.10.199`).
2. Navigate to **Virtual Services → Add New**.
3. Configure the Virtual Service:
   * **IP Address:** `10.10.110.199` (VLAN 110, via eth1 — see section 0. Not the WUI's `10.10.10.199`).
   * **Port:** `443`.
   * **Service Name:** `Blazor-App-VIP`.
   * Click **Add this Virtual Service**.
4. **Real Server Check Method** (under the VS's **Real Servers** section) — confirmed live: defaults to `HTTPS Protocol`, which will fail every health check and mark both real servers down, because the Blazor app serves plain HTTP on `:5000` (TLS is terminated at Kemp, never reaches the real servers — `docs/10-rollback.md`'s own health check is `curl http://localhost:5000/health`). **Change this to `HTTP Protocol`.**

---

## 2. Sticky Sessions & Persistence Setup (Crucial for Blazor Server WebSockets)

Blazor Server holds circuit state in memory. You **must** enable session persistence so the load balancer does not break active WebSocket connections:

1. In the Virtual Service settings, expand **Standard Options**.
2. **Persistence Options:**
   * **Mode:** confirmed live — on this LoadMaster license (Free/Trial VLM), the only options are `None` and `Source IP Address`; cookie-based `Super HTTP` persistence is not available (likely gated to a paid tier). Use **`Source IP Address`**.
   * **Timeout:** `1 Hour` (prevents mid-session circuit disconnects). This is less precise than a cookie (clients sharing a NAT/IP get pinned together), but adequate for a homelab audience.
3. Expand **Real Servers**:
   * **Scheduling Method:** `Round Robin` or `Least Connection`
4. Configure **Health Checking**:
   * **Health Check Protocol:** `HTTP` (see section 1 step 4 above)
   * **URL:** `/health`
   * **HTTP Method:** `GET`
   * **Interval / Timeout:** confirmed live — this license floors these at **`9`** seconds / **`4`** seconds respectively (the GUI rejects lower values); accept the floor rather than fighting it.
5. Add Real Servers:
   * Add IP `10.10.110.101` (Port `5000`)
   * Add IP `10.10.110.102` (Port `5000`)

Both Virtual Services will show **Down** in **Virtual Services → View/Modify Services** until the app is actually deployed to the real servers (runbook step 9) — nothing is listening on `:5000` yet at this point in the stand-up, so this is expected, not a misconfiguration.

---

## 3. HTTP → HTTPS redirect (port 80) — handled at the application layer, not Kemp

Add a second Virtual Service the same way: IP `10.10.110.199`, port `80`, name `Blazor-App-VIP-80`, same Real Servers/health check as above. Confirmed live: this Kemp license exposes no built-in "force HTTPS"/redirect toggle in Standard or Advanced Properties (Advanced Properties itself is also noticeably sparser on the `:443` VS than on `:80` until a certificate is bound — see section 4 below), and a native redirect may require the licensed ESP add-on.

Rather than chase a Kemp-side redirect feature, `:80` forwards to the same real servers as `:443`, and `src/BrewHouse/Program.cs` redirects at the application layer:

```csharp
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    KnownProxies = { System.Net.IPAddress.Parse("10.10.110.198") }, // Kemp's eth1 address
});
// ...
app.UseHttpsRedirection();
```

Kemp forwards to both real servers as plain HTTP regardless of which VIP port the client hit, so the app is the only thing that can tell `:80` and `:443` traffic apart — via the `X-Forwarded-Proto` header Kemp attaches on the `:443` VS once SSL Acceleration is enabled (section 4). `KnownProxies` is Kemp's **eth1 interface address** (`10.10.110.198`, the "Subnet Originating Requests" address it actually connects from), not the VIP.

---

## 4. Let's Encrypt wildcard cert on Kemp

See `docs/09-ssl-certificates.md` — bind the cert to the `10.10.110.199:443` Virtual Service (not the WUI's `10.10.10.199`). Confirmed live: several Advanced Properties fields on the `:443` VS (Content Switching, HTTP Header Modifications, "Add Header to Request", etc.) are hidden until a certificate is bound and SSL Acceleration is active — Kemp can't modify HTTP-layer content on traffic it isn't yet decrypting. If `X-Forwarded-Proto` doesn't appear automatically on real-server requests once the cert is bound, add it explicitly there via "Add Header to Request" → `X-Forwarded-Proto` / `https`.

---

## 5. Cloudflare Tunnel & WebSocket Keep-Alive Integration

In your `cloudflared` configuration, point the public hostname ingress rule at the **Kemp Virtual IP** — now on VLAN 110, and HTTPS since TLS terminates at Kemp:

```yaml
ingress:
  - hostname: homelab.smartsoftwarecoffee.com
    service: https://10.10.110.199:443
  - service: http_status:404
```

*Note: Blazor's `Program.cs` is configured with `KeepAliveInterval = 15s` to ensure Cloudflare Tunnels do not terminate quiet WebSocket connections after 100 seconds.*

---

## Alternative: HAProxy + Keepalived

If Kemp licensing ever becomes an issue, HAProxy + Keepalived is the documented free, open-source replacement (see ADR 15 in `docs/01-architecture-decisions.md`). HAProxy stick tables provide the session persistence Blazor Server requires for its long-lived WebSocket circuits, equivalent to Kemp's Sticky Sessions. Keepalived (VRRP) floats the `10.10.110.199` VIP between two load-balancer instances, preserving load-balancer redundancy. L7 health checks against the app's `/health` endpoint map directly onto HAProxy's `httpchk` configuration. Kemp LoadMaster remains the current choice for this architecture — none of the Free-tier limitations found above (Source IP persistence, 9s/4s health check floor, no built-in redirect) have proven to be an actual blocker.
