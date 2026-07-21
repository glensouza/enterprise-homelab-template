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
When running via the AppHost (`dotnet run --project src/RoadrunnerAuction.AppHost`), Aspire injects `OTEL_EXPORTER_OTLP_ENDPOINT` pointing at the built-in Aspire Dashboard, which renders logs, metrics, and traces for the app and its orchestrated containers in one view.

## 3. Production (Grafana Alloy on VLAN 30)
Provision a Debian LXC on Node 2 (VLAN 30 - `10.10.30.118`) hosting:
* **Grafana Alloy:** Receives OTLP from both Blazor nodes and fans out to the backends below.
* **Loki:** Log aggregation.
* **Tempo:** Distributed tracing.
* **Prometheus (+ Grafana):** Metrics storage and dashboards.

The `OTEL_EXPORTER_OTLP_ENDPOINT` value is rendered into `/etc/roadrunner/roadrunner.env` by the Infisical Agent and loaded by systemd via `EnvironmentFile=`.

## 4. Health Checks
The `/health` endpoint is unchanged: it performs deep checks against PostgreSQL, Garnet, and RabbitMQ and remains the probe target for Kemp L7 routing. It is independent of the telemetry pipeline.

---
### Source Material & Attribution
OpenTelemetry .NET documentation and Grafana Labs (Alloy/Loki/Tempo/Prometheus) documentation.
