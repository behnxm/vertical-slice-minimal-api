# 15. Semantic OpenTelemetry Observability and Custom Metrics

Date: 2026-10-31
Status: Accepted

## Context

Production services require distributed tracing, latency profiling, and operational metrics. Historically, teams installed proprietary APM agents (Datadog, New Relic, Dynatrace) which coupled codebases to proprietary vendor SDKs, incurred heavy memory footprints, and created vendor lock-in.

The Cloud Native Computing Foundation (CNCF) standardized telemetry via **OpenTelemetry (OTel)**.

## Decision

I chose **native OpenTelemetry instrumentation with semantic tracing, custom metrics, and standard OTLP export**.

1. **Distributed Tracing:** Traces ASP.NET Core incoming requests, outgoing HTTP calls, EF Core database queries (Npgsql), and custom application activities via `ActivitySource("TaskTracker.Api")`.
2. **Metrics Meter (`Meter("TaskTracker.Api")`):** Exposes high-fidelity domain and infrastructure counters and histograms:
   - `tasktracker.requests.total`: Counts requests partitioned by route, status code, and HTTP verb.
   - `tasktracker.requests.duration_ms`: High-resolution histogram of request latency.
   - `tasktracker.cache.hits` and `tasktracker.cache.misses`: Tracks HybridCache L1 and L2 performance.
   - `tasktracker.tasks.created` and `tasktracker.tasks.completed`: Tracks business activity.
3. **Vendor-Neutral Exporter:** Exports via standard OTLP (`OTEL_EXPORTER_OTLP_ENDPOINT`) over gRPC/HTTP to any compliant collector (Aspire Dashboard, Prometheus/Grafana, Jaeger, or Datadog OTel agent).

## Consequences

### Positive
- **Zero Vendor Lock-in:** Code targets the standard .NET `System.Diagnostics` tracing and metric APIs; switching backends requires only environment variable changes.
- **Deep Visibility:** End-to-end trace correlation links HTTP endpoints to specific EF Core SQL statements and Valkey cache lookups.
- **Low Overhead:** Zero-allocation in-process telemetry collection when sampling is enabled.

### Negative
- Requires configuring an OTel collector or dashboard in local development (provided via `aspire-dashboard` in `compose.yaml`).

### Alternatives Considered
- **Vendor APM SDKs (Datadog/New Relic):** Rejected due to proprietary dependencies and commercial lock-in.
