# Observability: OpenTelemetry & Grafana

The application emits all three telemetry signals via OpenTelemetry and exports them over the vendor-neutral OTLP protocol. The backend that receives the data differs per environment, but the application code is identical everywhere.

---

## 1. Application Instrumentation (OpenTelemetry)
The .NET app configures OpenTelemetry in `Program.cs`:
* **Logs:** `ILogger` output via OpenTelemetry, including scopes and formatted messages.
* **Metrics:** ASP.NET Core, HttpClient, and .NET Runtime instrumentation.
* **Traces:** ASP.NET Core and HttpClient instrumentation plus the `Npgsql` ActivitySource for database spans.
* **Export:** A single OTLP exporter (`UseOtlpExporter`) reads `OTEL_EXPORTER_OTLP_ENDPOINT`. If the variable is unset, export is a no-op.

## 2. Local Development (Aspire Dashboard)
When running via the AppHost (`dotnet run --project src/BrewHouse.AppHost`), Aspire injects `OTEL_EXPORTER_OTLP_ENDPOINT` pointing at the built-in Aspire Dashboard, which renders logs, metrics, and traces for the app and its orchestrated containers in one view.

## 3. Homelab (Grafana Alloy on VLAN 130)
Provision a Debian LXC on Node 2 (VLAN 130 - `10.10.130.118`) hosting:
* **Grafana Alloy:** Receives OTLP from both Blazor nodes and fans out to the backends below.
* **Loki:** Log aggregation.
* **Tempo:** Distributed tracing.
* **Prometheus (+ Grafana):** Metrics storage and dashboards.

The `OTEL_EXPORTER_OTLP_ENDPOINT` value is rendered into `/etc/brewhouse/brewhouse.env` by the Infisical Agent and loaded by systemd via `EnvironmentFile=`.

### Infrastructure monitoring (ADR 114)
* **node_exporter** runs on every Ansible-managed host (`roles/node-exporter`, `:9100`); Prometheus scrapes them all, with targets templated from the inventory. The VLAN 130 observability host reaches every VLAN through the existing `mgmt_to_any` policy.
* **Alertmanager** (`:9093` on the observability LXC) emails alerts through Gmail SMTP (`smtp.gmail.com:587`, STARTTLS). First alerts: host down, disk > 85 %, memory pressure, failed systemd unit, stale or missing pgBackRest backup. Rules: `ansible/roles/observability/files/alert-rules.yml`.
* **Dashboards as code:** Grafana provisions "Node Exporter Full" (grafana.com 1860) and "Proxmox via Prometheus" (10347) from the role; nothing is imported by hand.
* **devops** is scraped as well: the converge runs on it, so `site.yml` applies the node-exporter role to it over a local connection.
* **Proxmox:** `prometheus-pve-exporter` (localhost `:9221` on the observability LXC) reads the Proxmox API with a read-only token that `terraform/pve-exporter.tf` creates (ADR 115); alerts for node down, exporter down and storage > 85 %.
* **Secrets:** the GitHub secrets `ALERTMANAGER_SMTP_USER` / `ALERTMANAGER_SMTP_PASSWORD` (see `docs/13`).

### SNMP monitoring of the Synology NAS and the Kemp LoadMaster (ADR 116)
`prometheus-snmp-exporter` (localhost `:9116` on the observability LXC) polls both devices over **SNMPv3 `authPriv` (SHA + AES)**. One user, `SNMP_USER`, on both devices; the secrets are `SNMP_USER`, `SNMP_AUTH_PASSWORD`, `SNMP_PRIV_PASSWORD` (docs/13).

**Enable SNMPv3 on the Synology (DSM 7.x):** Control Panel -> Terminal & SNMP -> SNMP tab -> tick *Enable SNMP service* and *SNMPv3 service*; user name = `SNMP_USER`; protocol *SHA* + the auth password; tick *Enable SNMP privacy*, protocol *AES* + the privacy password; leave SNMPv1/v2c off; Apply. If the DSM firewall is on (Control Panel -> Security -> Firewall), allow UDP 161 from `10.10.130.118`.

**Enable SNMPv3 on the Kemp LoadMaster (WUI `https://10.10.10.199`, LMOS 7.2.63.2):** System Configuration -> Logging Options -> SNMP Options (the path was confirmed in the live WUI; it is not under Miscellaneous Options) -> tick *Enable SNMP*, then *Enable SNMP V3* (the SNMP Clients and Community String fields are unavailable in v3 mode, so v1/v2c is off and the device cannot restrict clients); on the SNMP V3 Options page set the user name = `SNMP_USER`, authentication protocol *SHA* + the auth password, *Enable Privacy* with protocol *AES* + the privacy password. Progress warns that enabling SNMP "causes a significant performance hit", so the Kemp job is polled once a minute. (Field labels per Progress' LoadMaster WUI guide.)

Passwords: 20+ characters, letters and digits only (DSM rejects `"`, `'`, `\` and spaces); `openssl rand -hex 16` gives a suitable one. Check from devops: `snmpwalk -v3 -l authPriv -u <user> -a SHA -A <auth> -x AES -X <priv> 10.10.10.90 1.3.6.1.4.1.6574.1`.

## 4. Health Checks
The `/health` endpoint is unchanged: it performs deep checks against PostgreSQL, Garnet, and RabbitMQ and remains the probe target for Kemp L7 routing. It is independent of the telemetry pipeline.

---
### Source Material & Attribution
OpenTelemetry .NET documentation and Grafana Labs (Alloy/Loki/Tempo/Prometheus) documentation.
