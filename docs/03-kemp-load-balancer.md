# Kemp LoadMaster Layer 7 Load Balancing & High Availability

To achieve high availability, zero-downtime rolling deployments, and stable WebSocket connections for Blazor Server, web traffic is routed through a Kemp LoadMaster appliance. 

---

## Architecture Flow

```text
[ Internet ] 
     │
[ Cloudflare Tunnel LXC: 10.10.10.5 ]
     │  (Routes to Kemp VIP)
[ Kemp Virtual Service VIP: 10.10.10.199 ] (Sticky Sessions Enabled)
     ├───> Real Server 1: Blazor LXC 01 (10.10.10.101:5000)
     └───> Real Server 2: Blazor LXC 02 (10.10.10.102:5000)
```

---

## 1. Kemp Virtual Service Setup

1. Log into the Kemp LoadMaster Web Console.
2. Navigate to **Virtual Services** -> **Add New**.
3. Configure the Virtual Service:
   * **IP Address:** `10.10.10.199` (Assigned in VLAN 10).
   * **Port:** `80` (or `443` if terminating SSL at Kemp).
   * **Service Name:** `Blazor-App-VIP`.
   * Click **Add this Virtual Service**.

---

## 2. Sticky Sessions & Persistence Setup (Crucial for Blazor Server WebSockets)

Blazor Server holds circuit state in memory. You **must** enable session persistence so the load balancer does not break active WebSocket connections:

1. In the Virtual Service settings, expand **Standard Options**.
2. **Persistence Options:**
   * **Mode:** Select `Super HTTP` (Inserts a Kemp persistence cookie) or `Source IP`.
   * **Timeout:** Set to `1 Hour` (Prevents mid-session circuit disconnects).
3. Expand **Real Servers**:
   * **Scheduling Method:** `Round Robin` or `Least Connection`
4. Configure **Health Checking**:
   * **Health Check Protocol:** `HTTP`
   * **URL:** `/health`
   * **HTTP Method:** `GET`
   * **Interval:** `5` seconds
   * **Timeout:** `2` seconds
5. Add Real Servers:
   * Add IP `10.10.10.101` (Port `5000`)
   * Add IP `10.10.10.102` (Port `5000`)

---

## 3. Cloudflare Tunnel & WebSocket Keep-Alive Integration

In your `cloudflared` configuration, point the public hostname ingress rule directly to the **Kemp Virtual IP**:

```yaml
ingress:
  - hostname: app.yourdomain.com
    service: http://10.10.10.199:80
  - service: http_status:404
```

*Note: Blazor's `Program.cs` is configured with `KeepAliveInterval = 15s` to ensure Cloudflare Tunnels do not terminate quiet WebSocket connections after 100 seconds.*

---

## Alternative: HAProxy + Keepalived

If Kemp licensing ever becomes an issue, HAProxy + Keepalived is the documented free, open-source replacement (see ADR 15 in `docs/01-architecture-decisions.md`). HAProxy stick tables provide the session persistence Blazor Server requires for its long-lived WebSocket circuits, equivalent to Kemp's Sticky Sessions. Keepalived (VRRP) floats the `10.10.10.199` VIP between two load-balancer instances, preserving load-balancer redundancy. L7 health checks against the app's `/health` endpoint map directly onto HAProxy's `httpchk` configuration. Kemp LoadMaster remains the current choice for this architecture.
