# Observability: Grafana Loki & Serilog

To trace logs across our distributed HA nodes, we use Grafana Loki.

## 1. Loki Provisioning
Provision a Debian LXC on Node 2 (VLAN 30 - `10.10.30.118`).
Install Loki and Grafana.

## 2. Serilog Integration
The `.NET` application uses the `Serilog.Sinks.Grafana.Loki` NuGet package.
On boot, it pushes all structured JSON logs directly to the Loki endpoint defined in `appsettings.json`.

---
### Source Material & Attribution
Grafana Labs Loki documentation and Serilog community sinks.
