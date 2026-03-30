# grafana-playground

A collection of Docker Compose projects for exploring observability with the **Grafana LGTM stack** (Loki, Grafana, Tempo, Prometheus) and **.NET 10 microservices**.

Each project spins up a complete, self-contained observability environment — Grafana pre-configured with all datasources, an OpenTelemetry Collector as the central telemetry hub, and two communicating .NET services that generate traces, metrics, and logs.

The two projects are functionally identical from an observability standpoint. They differ only in **how instrumentation is added to the .NET applications**, making them useful for comparing the two main approaches side by side.

---

## Projects

### [dotnet-project-compose](./dotnet-project-compose/README.md)

Instrumentation via **OpenTelemetry SDK NuGet packages**. The applications explicitly configure the OTel SDK in `Program.cs` using `AddOpenTelemetry()`, with instrumentation libraries for ASP.NET Core, HttpClient, and the .NET runtime.

- Full control over what gets instrumented and how
- SDK version pinned via NuGet
- Requires modifying application source code

```bash
cd dotnet-project-compose
docker compose up --build
```

---

### [dotnet-autoinstr-compose](./dotnet-autoinstr-compose/README.md)

Instrumentation via the **OpenTelemetry .NET Auto-Instrumentation agent**. The applications contain zero OpenTelemetry code — no packages, no setup. The CLR profiler and startup hook are injected at the Docker image layer and activated via environment variables.

- Zero application code changes
- Agent installed in the Dockerfile, configured via env vars
- Useful for instrumenting apps you cannot or do not want to modify

```bash
cd dotnet-autoinstr-compose
docker compose up --build
```

---

## Stack overview

Both projects share the same infrastructure:

| Component | Role |
|-----------|------|
| **Grafana** | UI — Explore, dashboards, datasource links |
| **Tempo** | Distributed trace storage and query |
| **Loki** | Log aggregation and query |
| **Prometheus** | Metrics storage and query |
| **OpenTelemetry Collector** | Receives OTLP from apps, routes to Tempo / Loki / Prometheus |

Grafana is pre-provisioned with all three datasources and cross-signal navigation:
- **Trace → Logs**: jump from a Tempo span directly to Loki logs filtered by `traceId`
- **Trace → Metrics**: link spans to Prometheus RED metrics
- **Service Map**: visualize service dependencies via Tempo's span metrics

## Approach comparison

| | dotnet-project-compose | dotnet-autoinstr-compose |
|---|---|---|
| OTel setup | `Program.cs` | Dockerfile + env vars |
| NuGet packages | Yes | None |
| Code changes required | Yes | No |
| Custom spans/metrics | Full SDK | `ActivitySource` + `OTEL_DOTNET_AUTO_TRACES_ADDITIONAL_SOURCES` |
| Good for | New services, full control | Legacy apps, zero-touch instrumentation |
