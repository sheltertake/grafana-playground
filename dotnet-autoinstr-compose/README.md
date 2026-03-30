# dotnet-autoinstr-compose

A Docker Compose playground for observability with the **Grafana LGTM stack** (Loki, Grafana, Tempo, Mimir/Prometheus) and two **.NET 10 microservices instrumented automatically** using the OpenTelemetry .NET Auto-Instrumentation agent.

The key characteristic of this project: the application code contains **zero OpenTelemetry references**. No NuGet packages, no `AddOpenTelemetry()` calls, no exporters configured in code. The CLR profiler and startup hook inject all instrumentation at runtime, entirely from the Dockerfile and environment variables.

This project is the "zero-code" counterpart to [`dotnet-project-compose`](../dotnet-project-compose/README.md), which achieves the same result using the OpenTelemetry SDK packages explicitly.

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

Both services are plain ASP.NET Core Minimal API apps targeting **net10.0**. Their `.csproj` files have no external dependencies beyond the framework itself. `Program.cs` contains only business logic.

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

## How auto-instrumentation works

### The agent installation (Dockerfile)

The Dockerfile adds an extra stage to the standard multi-stage .NET build:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# Download and install the OpenTelemetry .NET auto-instrumentation agent
RUN apt-get update && \
    apt-get install -y --no-install-recommends curl unzip && \
    curl -sSfL https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/releases/latest/download/otel-dotnet-auto-install.sh \
      -o /tmp/otel-install.sh && \
    OTEL_DOTNET_AUTO_HOME=/otel sh /tmp/otel-install.sh && \
    rm /tmp/otel-install.sh && \
    apt-get remove -y curl unzip && \
    rm -rf /var/lib/apt/lists/*
```

The install script downloads the correct binary bundle for the platform (linux-glibc-x64 in this case) and unpacks it to `/otel`. The bundle contains:
- A **native shared library** (`.so`) — the CLR profiler that hooks into the .NET runtime
- A set of **managed assemblies** — the actual instrumentation libraries (ASP.NET Core, HttpClient, EF Core, etc.)
- **AdditionalDeps** and **store** directories — additional dependencies injected into the app's dependency resolution

### Activating the agent (environment variables)

The following `ENV` instructions in the Dockerfile activate the agent for every process started from the image:

```dockerfile
ENV OTEL_DOTNET_AUTO_HOME=/otel

# Enables the CLR profiler API
ENV CORECLR_ENABLE_PROFILING=1

# Identifies which profiler to load (fixed GUID for OTel auto-instrumentation)
ENV CORECLR_PROFILER={918728DD-259F-4A6A-AC2B-B85E1B658318}

# Path to the native profiler shared library
ENV CORECLR_PROFILER_PATH=/otel/linux-x64/OpenTelemetry.AutoInstrumentation.Native.so

# Injects managed OTel assemblies into the app's dependency graph
ENV DOTNET_ADDITIONAL_DEPS=/otel/AdditionalDeps
ENV DOTNET_SHARED_STORE=/otel/store

# Runs the OTel startup hook before the app's entry point
ENV DOTNET_STARTUP_HOOKS=/otel/net/OpenTelemetry.AutoInstrumentation.StartupHook.dll
```

When the container starts, the .NET runtime:
1. Loads the **native profiler** (`CORECLR_PROFILER_PATH`) via the CLR Profiling API
2. Executes the **startup hook** (`DOTNET_STARTUP_HOOKS`) before `Program.cs` runs
3. The hook bootstraps the OpenTelemetry SDK, configures all instrumentation libraries, and sets up the OTLP exporter — all based on environment variables

The application code never knows any of this happened.

### What gets instrumented automatically

The auto-instrumentation agent instruments the following out of the box, without any code changes:

- **Incoming HTTP requests** (ASP.NET Core) — a span is created for every request
- **Outgoing HTTP requests** (`HttpClient`) — spans for every outgoing call, with W3C `traceparent` header injection for distributed tracing
- **`ILogger` logs** — log entries emitted via the standard `ILogger` interface are captured and exported as OTLP logs, including the `traceId` and `spanId` of the active span
- **.NET runtime metrics** — GC collections, thread pool usage, heap size
- **ASP.NET Core metrics** — request rate, duration histograms, active connections

### Configuring the agent via docker-compose environment variables

The agent behavior is controlled entirely via standard `OTEL_*` environment variables set in `docker-compose.yml`:

```yaml
environment:
  - OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317
  - OTEL_EXPORTER_OTLP_PROTOCOL=grpc
  - OTEL_SERVICE_NAME=dotnet-app
  - OTEL_DOTNET_AUTO_LOGS_INCLUDE_FORMATTED_MESSAGE=true
```

- `OTEL_EXPORTER_OTLP_ENDPOINT` — where to send telemetry (the OTLP Collector)
- `OTEL_EXPORTER_OTLP_PROTOCOL` — `grpc` or `http/protobuf`
- `OTEL_SERVICE_NAME` — the service name that appears in all traces, metrics, and logs
- `OTEL_DOTNET_AUTO_LOGS_INCLUDE_FORMATTED_MESSAGE` — includes the rendered log message (with parameter substitution) alongside the structured log template

### Distributed trace propagation

`HttpClient` instrumentation automatically injects the **W3C `traceparent` header** on all outgoing requests. When `dotnet-app` calls `backend`, the agent in `backend` reads the incoming header and continues the same trace. The result is a single trace with spans from both services, visible as a waterfall in Grafana Tempo — with zero code written to make this happen.

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
- **Trace to Logs**: clicking a span opens Loki filtered by `traceId` and `service_name`, with a ±1 minute time window around the span
- **Trace to Metrics**: links spans to Prometheus metrics
- **Service Map**: visualizes service dependencies using Tempo's span metrics in Prometheus

---

## Getting started

```bash
cd dotnet-autoinstr-compose
docker compose up --build
```

The first build takes longer than usual because the agent download happens inside Docker. Subsequent builds are cached.

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

## Extending with custom spans and metrics

The auto-instrumentation agent does not prevent you from adding custom instrumentation. You can use `System.Diagnostics.ActivitySource` and `System.Diagnostics.Metrics.Meter` directly — both are part of the .NET BCL and require no NuGet packages.

You do **not** need to call `AddOpenTelemetry()` in `Program.cs`. The agent already owns the `TracerProvider` and `MeterProvider`. You only need to register your source names via environment variables so the agent picks them up:

```csharp
// In your app code — no OTel imports required
static readonly ActivitySource MyActivities = new("MyApp.Orders");
static readonly Meter MyMetrics = new("MyApp.Orders");
static readonly Counter<int> OrdersCreated = MyMetrics.CreateCounter<int>("orders.created");

app.MapPost("/orders", () =>
{
    using var span = MyActivities.StartActivity("ProcessOrder");
    span?.SetTag("order.priority", "high");

    OrdersCreated.Add(1, new KeyValuePair<string, object?>("priority", "high"));
    return Results.Ok();
});
```

Then tell the agent to include your sources:

```yaml
environment:
  - OTEL_DOTNET_AUTO_TRACES_ADDITIONAL_SOURCES=MyApp.Orders
  - OTEL_DOTNET_AUTO_METRICS_ADDITIONAL_SOURCES=MyApp.Orders
```

The custom spans and metrics will appear in Grafana alongside the automatically generated ones.

---

## Comparison with dotnet-project-compose

| | dotnet-autoinstr-compose | dotnet-project-compose |
|---|---|---|
| Instrumentation method | Runtime agent injected via Dockerfile | Manual SDK setup in `Program.cs` |
| OTel NuGet packages | None | Required |
| Application code changes | Zero | Yes — `AddOpenTelemetry()` setup |
| Custom spans/metrics | Via `ActivitySource`/`Meter` + env vars | Full SDK available |
| Agent version pinning | Via GitHub release in Dockerfile | Via NuGet package versions |
| Startup overhead | Slight (CLR profiler attach) | Minimal |
| Best suited for | Apps you can't modify, legacy code, standardized fleet instrumentation | New services, fine-grained control, custom telemetry |

Both approaches produce identical telemetry in Grafana. Auto-instrumentation is particularly useful when you want to enforce a consistent observability baseline across many services without touching each codebase.
