# Stria System Analysis and Enhancement Handoff

This document describes the repository as inspected on 2026-11-13. It is a current-state handoff for future development work. The source code and `README.md` are authoritative if this document later becomes stale.

## Project purpose

Stria is a .NET 10 chaos-engineering control-plane demo. An operator uses a Blazor dashboard or HTTP API to create bounded fault experiments for registered services. Stria persists those experiments in PostgreSQL and provides a polling directive endpoint that participating services use to apply faults locally.

The repository includes one participating data-plane service, `sample-payment-gateway`. It polls Stria and can actually apply latency and HTTP-failure faults to its `/work` endpoint. The remaining registered targets are currently control-plane entries only.

## Current architecture

```text
Browser
  └── Stria dashboard (Blazor WebAssembly + interactive server components)
        └── Stria host (ASP.NET Core)
              ├── POST /api/v1/chaos/inject
              ├── GET  /api/v1/chaos/faults
              ├── GET  /api/v1/chaos/directives/{service}
              ├── DatabaseChaosOrchestrator
              │     └── PostgreSQL / ChaosExperiments
              ├── ExperimentExpiryService
              └── ExperimentRetentionService

sample-payment-gateway
  └── polls Stria directives every second
        └── applies fault middleware to /work
```

## Runtime topology

`compose.yaml` starts three services:

| Service | Responsibility | Reachability |
|---|---|---|
| `stria-server` | Dashboard, control API, directive API, background workers | `http://localhost:8080`, bound to loopback |
| `db` | PostgreSQL 16 experiment storage | Internal Compose network only |
| `sample-payment-gateway` | Demo data-plane participant | `http://localhost:8081`, bound to loopback |

The host runs plain HTTP on container port `8080`; Compose publishes that same port to `127.0.0.1:8080`. This replaces the old, incorrect `8443:8443` mapping. `sample-payment-gateway` reaches Stria over the private Compose network at `http://stria-server:8080`.

Compose applies EF Core migrations on startup for local convenience. This is suitable for the demo but should be replaced by a controlled migration step before a multi-replica production deployment.

## Fault model

Stria recognizes these target names, case-insensitively:

- `PaymentGateway`
- `AuthService`
- `InventoryAPI`

The API supports these fault types:

| Fault | Parameters | Current execution behavior |
|---|---|---|
| `Latency` | Required delay: 1–60,000 ms; optional 1–100% request selection | Executed by the sample payment gateway with non-blocking `Task.Delay` |
| `HttpFailure` | Optional 500–599 response (default 503); optional 1–100% request selection | Executed by the sample payment gateway by short-circuiting requests |
| `CpuSpike` | Lease duration only | Persisted and distributed, but not executed by the sample service |
| `None` | No additional parameters | Clears the target's active experiment |

Every non-clear experiment has a lease of 1–3,600 seconds, defaulting to 300 seconds. The API defaults `Percent` to 100. Only one active experiment may exist per target; a new command clears or replaces any existing active experiment for that target.

## Persistence and lifecycle

`DatabaseChaosOrchestrator` is a singleton using `IDbContextFactory<StriaDbContext>` to create short-lived EF Core contexts. It stores every experiment in PostgreSQL rather than holding active state only in memory.

`ChaosExperiments` includes target, fault details, status, creation time, expiration time, triggering identity, and clearing details. Database constraints enforce fault parameter invariants and a partial unique index ensures no target has more than one active row.

An experiment is considered active only when its status is `Active` and its lease has not expired. Reads enforce that rule themselves, so an expired fault stops being served immediately even if the cleanup worker has not run. `ExperimentExpiryService` periodically marks expired rows `Cleared` as `system:expiry`. `ExperimentRetentionService` periodically removes only cleared rows according to configurable age and count limits.

## Data-plane behavior

The sample service uses `FaultPoller` to request `/api/v1/chaos/directives/PaymentGateway` once per second by default.

- `200 OK` with a directive: replace the local directive lease.
- `204 No Content`: clear the local directive immediately.
- A network/control-plane failure: retain the most recent directive only until its already-issued local lease expires.

The local lease uses a monotonic `TimeProvider` timestamp, so wall-clock changes and clock skew cannot extend a fault. Fault middleware excludes `/healthz`, preserving service health checks while faults affect `/work`. It caps concurrent delayed requests to prevent a latency experiment from exhausting the sample process.

The sample service does not execute `CpuSpike`; it logs that the fault is unsupported and serves normally.

## HTTP API

- `GET /api/v1/chaos/faults` returns all active, unexpired experiments.
- `GET /api/v1/chaos/directives/{service}` returns an active `FaultDirective`, `204 No Content` when none is active, or `404 Not Found` for an unknown target.
- `POST /api/v1/chaos/inject` validates and activates or replaces an experiment. A request with `faultType: "None"` clears the active experiment.

For example:

```json
{
  "targetService": "PaymentGateway",
  "faultType": "Latency",
  "latencyMs": 750,
  "durationSeconds": 120,
  "percent": 50
}
```

The server ignores the request body's `TriggeredBy` field and currently records the actor as `anonymous`, because authentication has not been configured.

## User interface

The home page presents fixed controls for each registered target:

- Payment Gateway: 3-second latency.
- Auth Service: HTTP 503.
- Inventory API: CPU spike.

It also polls active faults every three seconds and presents a per-service status bar and clear action. The UI currently shows only target, fault type, and latency; it does not display duration, percentage, status code, audit history, or target acknowledgement.

## Known gaps and risks

1. **No authentication or authorization.** All control and directive endpoints are unauthenticated. This is acceptable only for a loopback-only local demo, not an internal shared or public deployment.
2. **Only one executable demo target.** The Compose stack runs only `sample-payment-gateway`. `AuthService` and `InventoryAPI` have no participating service.
3. **`CpuSpike` is not implemented.** Stria can record and distribute it, but no service executes it.
4. **No target acknowledgement.** Stria knows an experiment was requested, not whether a target received or applied the directive.
5. **Server-rendered API client is hard-coded.** `../Stria.API` registers its scoped `HttpClient` with `http://localhost:8080`, which may not work for every local launch or future HTTPS deployment configuration.
6. **HTTPS is not configured for Compose.** The local stack intentionally exposes loopback HTTP. A hosted deployment needs TLS termination and secure external ingress.
7. **No test project or CI pipeline.** The repository has no automated unit, integration, Compose smoke, image, or deployment validation.
8. **Migration-on-startup is demo-oriented.** A controlled migration job or release step is safer for production rollouts.
9. **Operational logging needs refinement.** `TrafficSimulatorService` does not generate traffic or apply faults; it produces routine logs and logs active faults at `Critical`, which can create alert noise.
10. **The dashboard surfaces raw exception text.** Operator-facing errors should be concise while diagnostic detail stays in logs.

## Recommended enhancement sequence

1. Add a solution test suite for validation rules, persistence, lease expiry, retention, and API behavior.
2. Add `/healthz` and `/readyz`, configuration validation, and remove or replace `TrafficSimulatorService`.
3. Secure the control plane with OpenID Connect, operator/viewer roles, and server-derived audit identity.
4. Add a registered service model with per-service fault capabilities and limits instead of hard-coded target names.
5. Extract a supported data-plane integration package and add acknowledgement/status reporting from participating services.
6. Improve the dashboard with experiment duration, percentage, status code, expiry countdown, lifecycle history, and target execution state.
7. Add OpenTelemetry metrics/traces, structured logs, a global emergency kill switch, and environment-specific safety caps.
8. Add CI to build, test, scan, publish immutable images, and perform a staged smoke deployment.
9. Add infrastructure-as-code and deploy the dashboard/control plane behind authenticated HTTPS ingress while keeping data-plane targets private.

## Working conventions

- Treat fault injection as privileged control-plane activity.
- Maintain lease-based automatic expiry; never rely on manual clearing for safety.
- Keep execution mechanisms in the target data plane, not in the Stria host process.
- Preserve `/healthz` as an unfaulted path for participating services.
- Make target capabilities explicit and reject unsupported faults instead of claiming they ran.
- Do not claim production readiness until access control, testing, observability, deployment safety, and rollback procedures are implemented and verified.
