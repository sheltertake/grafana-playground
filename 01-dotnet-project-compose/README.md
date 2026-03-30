# dotnet-project-compose

A Docker Compose playground for observability with the **Grafana LGTM stack** (Loki, Grafana, Tempo, Mimir/Prometheus) and two **.NET 10 microservices instrumented manually** using the OpenTelemetry SDK NuGet packages.

This project is the "explicit" counterpart to [`dotnet-autoinstr-compose`](.../02-dotnet-autoinstr-compose/README.md), which achieves the same result without any OpenTelemetry code in the application.

---

## What's inside

### Services

| Service | Port | Description |
|---------|------|-------------|
| `grafana` | 3000 | Dashboards and Explore UI. Anonymous access pre-configured as Admin. |
| `prometheus` | 9090 | Metrics storage. Scrapes the OTLP Collector's Prometheus endpoint. Remote write receiver enabled for Tempo metrics generator. |
| `loki` | 3100 | Log aggregation. Receives logs from the OTLP Collector via OTLP HTTP. |
| `tempo` | 3200 | Distributed trace storage. Receives traces from the OTLP Collector. |
| `otel-collector` | 4317 (gRPC), 4318 (HTTP), 8889 (Prometheus) | Central telemetry hub. Receives OTLP from the .NET apps and fans out to Tempo, Loki, and Prometheus. |
| `dotnet-app` | 8080 | Frontend .NET 10 service. Calls `backend` on some routes. |
| `backend` | 8080 (internal) | Backend .NET 10 service. Called by `dotnet-app`. |

### .NET applications

Both services are ASP.NET Core Minimal API apps targeting **net10.0** and using the **OpenTelemetry SDK** packages (v1.15.x). The OpenTelemetry setup is done explicitly in `Program.cs` using the `AddOpenTelemetry()` builder extension.

#### dotnet-app endpoints

| Endpoint | Description |
|----------|-------------|
| `GET /` | Health check |
| `GET /weather` | Returns random weather forecasts. Generates a trace span and an `Information` log. |
| `GET /slow` | Waits a random time between 200ms and 2s. Useful to observe latency in traces. |
| `GET /chain` | Calls `backend /data` via `HttpClient`. Generates a **distributed trace** across both services. |
| `GET /chain/slow` | Calls `backend /slow-data`. Distributed trace with variable latency. |
| `GET /error` | Throws an `InvalidOperationException`. Generates an error span and an `Error` log. |

#### backend endpoints

| Endpoint | Description |
|----------|-------------|
| `GET /` | Health check |
| `GET /data` | Returns a random value. Called by `dotnet-app /chain`. |
| `GET /slow-data` | Returns after a random delay (200–1500ms). Called by `dotnet-app /chain/slow`. |

---

## How instrumentation works

The OpenTelemetry setup lives entirely in `Program.cs` of each service:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(m => m
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter());

builder.Logging.AddOpenTelemetry(o =>
{
    o.IncludeFormattedMessage = true;
    o.IncludeScopes = true;
    o.AddOtlpExporter();
});
```

The OTLP exporter is configured via environment variables — no hardcoded endpoints in code:

```yaml
environment:
  - OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317
  - OTEL_EXPORTER_OTLP_PROTOCOL=grpc
  - OTEL_SERVICE_NAME=dotnet-app
```

### Distributed trace propagation

`AddHttpClientInstrumentation()` automatically injects the **W3C `traceparent` header** on outgoing HTTP requests and extracts it on incoming ones. This means that a call to `/chain` generates a single trace with two spans: one for `dotnet-app` and one for `backend`, linked together in Grafana Tempo's waterfall view.

---

## Observability pipeline

```
dotnet-app  ─┐
              ├──► otel-collector (OTLP gRPC :4317)
backend     ─┘         │
                        ├──► Tempo     (traces,   OTLP HTTP)
                        ├──► Loki      (logs,     OTLP HTTP)
                        └──► Prometheus (metrics, scrape :8889)

Tempo ──► Prometheus (span metrics + service graph via remote write)

Grafana ──► queries all three backends
```

### OTLP Collector configuration highlights

- **Receivers:** OTLP gRPC (4317) and HTTP (4318)
- **Processors:**
  - `batch` — buffers and batches telemetry before exporting
  - `resource` — injects `loki.resource.labels: service.name` so Loki indexes the service name as a queryable label
- **Exporters:**
  - `otlphttp/tempo` → `http://tempo:4318`
  - `otlphttp/loki` → `http://loki:3100/otlp`
  - `prometheus` → exposes scraped metrics on `:8889`

### Tempo metrics generator

Tempo is configured with the `service-graphs` and `span-metrics` processors (via tenant overrides). It writes RED metrics (Rate, Errors, Duration) to Prometheus via remote write, enabling the **Service Graph** visualization in Grafana.

### Grafana datasource links

The Tempo datasource is pre-configured with:
- **Trace to Logs**: clicking a span jumps to Loki filtered by `traceId` and `service_name`, with a ±1 minute time window around the span
- **Trace to Metrics**: links spans to Prometheus metrics
- **Service Map**: visualizes service dependencies using Tempo's span metrics in Prometheus

---

## NuGet packages

Each .NET service references these packages:

```xml
<PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.15.1" />
<PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.15.1" />
<PackageReference Include="OpenTelemetry.Instrumentation.Http" Version="1.15.0" />
<PackageReference Include="OpenTelemetry.Instrumentation.Runtime" Version="1.15.0" />
<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.15.1" />
```

- **Extensions.Hosting** — integrates the OTel SDK lifecycle with the .NET generic host
- **Instrumentation.AspNetCore** — auto-instruments incoming HTTP requests (creates spans, records HTTP metrics)
- **Instrumentation.Http** — auto-instruments outgoing `HttpClient` requests and propagates trace context
- **Instrumentation.Runtime** — exports .NET runtime metrics (GC, thread pool, memory)
- **Exporter.OpenTelemetryProtocol** — OTLP exporter over gRPC or HTTP/protobuf

---

## Getting started

```bash
cd dotnet-project-compose
docker compose up --build
```

Then hit some endpoints to generate telemetry:

```bash
curl http://localhost:8080/weather
curl http://localhost:8080/chain
curl http://localhost:8080/chain/slow
curl http://localhost:8080/error
```

Open Grafana at **http://localhost:3000** and go to **Explore**:

- **Loki** → `{service_name="dotnet-app"}` to see logs
- **Tempo** → search by service name or trace ID; open a trace to see the cross-service waterfall
- **Prometheus** → query `traces_spanmetrics_calls_total` or `traces_service_graph_request_total` for RED metrics

---

## Comparison with dotnet-autoinstr-compose

| | dotnet-project-compose | dotnet-autoinstr-compose |
|---|---|---|
| Instrumentation method | Manual SDK setup in `Program.cs` | Runtime agent injected via Dockerfile |
| OTel NuGet packages | Required | None |
| Application code changes | Yes — `AddOpenTelemetry()` setup | Zero |
| Custom spans/metrics | Full SDK available | Via `ActivitySource`/`Meter` + env vars |
| Agent version pinning | Via NuGet package versions | Via GitHub release in Dockerfile |
| Startup overhead | Minimal | Slight (CLR profiler attach) |

Both approaches produce identical telemetry in Grafana. The choice between them depends on how much control you need over instrumentation and whether you can modify the application source.
