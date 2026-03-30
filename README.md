# grafana-playground

A collection of Docker Compose projects for exploring observability with the **Grafana LGTM stack** (Loki, Grafana, Tempo, Prometheus) and **.NET 10 microservices**.

Each project spins up a complete, self-contained observability environment — Grafana pre-configured with all datasources, an OpenTelemetry Collector as the central telemetry hub, and communicating .NET services that generate traces, metrics, and logs.

---

## Projects

### [01-dotnet-project-compose](./01-dotnet-project-compose/README.md)

Instrumentation via **OpenTelemetry SDK NuGet packages**. The applications explicitly configure the OTel SDK in `Program.cs` using `AddOpenTelemetry()`, with instrumentation libraries for ASP.NET Core, HttpClient, and the .NET runtime.

- Full control over what gets instrumented and how
- SDK version pinned via NuGet
- Requires modifying application source code

```bash
cd 01-dotnet-project-compose
docker compose up --build
```

---

### [02-dotnet-autoinstr-compose](./02-dotnet-autoinstr-compose/README.md)

Instrumentation via the **OpenTelemetry .NET Auto-Instrumentation agent**. The applications contain zero OpenTelemetry code — no packages, no setup. The CLR profiler and startup hook are injected at the Docker image layer and activated via environment variables.

- Zero application code changes
- Agent installed in the Dockerfile, configured via env vars
- Useful for instrumenting apps you cannot or do not want to modify

```bash
cd 02-dotnet-autoinstr-compose
docker compose up --build
```

---

### [03-dotnet-faro-compose](./03-dotnet-faro-compose/README.md)

Adds a **React/Vite/TypeScript SPA** to the stack, instrumented with **Grafana Faro**. Captures browser-side telemetry (Web Vitals, JS errors, fetch traces) and connects it to the backend traces via W3C `traceparent` header propagation — giving a single end-to-end trace from browser click to backend span.

- Frontend observability with Grafana Faro SDK
- **Grafana Alloy** as the Faro receiver (browser → Alloy → Loki/Tempo)
- End-to-end distributed traces: browser → dotnet-app → backend

```bash
cd 03-dotnet-faro-compose
docker compose up --build
```

---

## Stack overview

| Component | Role |
|-----------|------|
| **Grafana** | UI — Explore, dashboards, datasource links |
| **Tempo** | Distributed trace storage and query |
| **Loki** | Log aggregation and query |
| **Prometheus** | Metrics storage and query |
| **OpenTelemetry Collector** | Receives OTLP from .NET apps, routes to Tempo / Loki / Prometheus |
| **Grafana Alloy** *(03 only)* | Receives Faro pushes from the browser, routes to Loki / OTel Collector |

Grafana is pre-provisioned with all datasources and cross-signal navigation:
- **Trace → Logs**: jump from a Tempo span to Loki logs filtered by `traceId`
- **Trace → Metrics**: link spans to Prometheus RED metrics
- **Service Map**: visualize service dependencies via Tempo's span metrics

---

## Comparison

| | 01 SDK | 02 Auto-Instrumentation | 03 Faro |
|---|---|---|---|
| Backend instrumentation | NuGet SDK | CLR agent | NuGet SDK |
| Frontend instrumentation | — | — | Grafana Faro |
| Code changes required | Yes | No | Yes (faro.ts) |
| End-to-end browser traces | No | No | Yes |
| Extra infrastructure | — | — | Grafana Alloy |
