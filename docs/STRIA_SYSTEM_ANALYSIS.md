# Stria System Analysis and Enhancement Handoff

This document summarizes the repository as inspected on 2026-11-09. It is intended as context for Claude before the first system enhancement iteration.

## Project purpose

Stria is a small chaos-control dashboard. Operators can enable a fault for one of three named services, view active fault state, and clear a fault. The current implementation is a prototype: it stores state in process memory and reports simulated traffic activity, but it does not yet apply faults to real dependencies or generate traffic.

## Architecture map

```text
Browser
  └── Stria.Client (Blazor WebAssembly UI)
        ├── POST /api/chaos/inject
        └── GET  /api/chaos/faults (polls every 3 seconds)
              └── Stria host (ASP.NET Core, Stria/Program.cs)
                    ├── IChaosOrchestrator
                    │     └── DatabaseChaosOrchestrator (currently in-memory)
                    └── TrafficSimulatorService (hosted background service)
```

The host project references `Stria.Client` and serves its static WebAssembly assets. `compose.yaml` currently runs only the host container. Its intended local HTTPS address is `https://localhost:8443`; HTTP port 8080 is internal to the container. Compose requires `STRIA_HTTPS_CERT_PATH` and `STRIA_HTTPS_CERT_PASSWORD` from the environment. It also mounts a named volume for the ASP.NET Core Data Protection key ring.

## Layer responsibilities and files

| Layer | Files | Current responsibility |
|---|---|---|
| Host/bootstrap/API | `Stria/Program.cs` | Registers Razor component hosting and services, configures Data Protection, maps `GET /api/chaos/faults` and `POST /api/chaos/inject`, validates service/fault keys and latency bounds. |
| Domain | `Stria/Domain/ChaosExperiment.cs` | Experiment state: target, fault type, status, timestamps, trigger metadata. |
| Request contract | `Stria/DTOs/FaultInjectionRequest.cs` | Injection request shape: target, fault type, optional latency, trigger label. |
| Orchestration boundary | `Stria/Services/IChaosOrchestrator.cs` | Contract to inject a fault and query active state. |
| Orchestration implementation | `Stria/Services/DatabaseChaosOrchestrator.cs` | Despite its name, stores only the latest experiment per service in a `ConcurrentDictionary`; no database is used. |
| Background worker | `Stria/TrafficSimulatorService.cs` | Checks configured service keys every 2.5 seconds and logs active faults or routine-traffic messages. It does not currently create traffic or enact a fault. |
| Client entry page | `Stria.Client/Pages/Home.razor` | Fault injection controls and operator feedback. |
| Client telemetry | `Stria.Client/Components/ActiveFaultsTable.razor` | Polls active faults every three seconds, shows service status bars/table, and posts clear requests. |
| UI styles/layout | `Stria.Client/Pages/Home.razor.css`, `Stria.Client/Components/ActiveFaultsTable.razor.css`, `Stria.Client/Layout/MainLayout.razor*` | Custom console styling and the app shell. |
| Container/runtime | `compose.yaml`, `Stria/Dockerfile`, `Stria.Client/Dockerfile` | Compose uses the host Dockerfile; the separate Nginx client image is not part of current Compose deployment. |

## API contract

- `GET /api/chaos/faults`: returns the current active experiments.
- `POST /api/chaos/inject`: accepts `{ targetService, faultType, latencyMs?, triggeredBy? }`; returns 202 when accepted.
- Registered targets: `PaymentGateway`, `AuthService`, `InventoryAPI` (case-insensitive).
- Supported faults: `Latency`, `HttpFailure`, `CpuSpike`, `None` (case-insensitive).
- Latency is required for `Latency` and must be 1–60,000 ms; it is rejected for other fault types.
- `FaultType = "None"` clears the target's current state.

## Important known gaps and risks

1. **No authentication/authorization.** Both API endpoints are unauthenticated. Compose binds HTTPS to loopback, which limits exposure in the local Compose setup, but this is not suitable for deployment to a shared or public network.
2. **Faults are state only.** `HttpFailure`, `CpuSpike`, and `Latency` currently only become labels in memory and logs. No service middleware, dependency proxy, traffic generator, or resource stressor applies them.
3. **State is volatile and singular.** Restarting the host clears the in-memory registry. The implementation stores one current experiment per service rather than an experiment history. The class name `DatabaseChaosOrchestrator` and EF/Npgsql package references imply persistence that does not exist.
4. **Server-side `HttpClient` is hard-coded.** `Stria/Program.cs` uses `http://localhost:8080`. This matches the container's private HTTP listener, but it will fail in local development on the configured launch ports unless server-interactive code routes to the appropriate origin. Prefer avoiding loopback HTTP calls from server-rendered code, or configure a reliable host-aware base address.
5. **Polling and error handling.** The WASM component polls correctly for local operation, but errors include exception text in the UI. Consider logging details server-side and displaying a short operator-safe message. A shared refresh/event mechanism could avoid waiting up to three seconds after Home-page injections.
6. **Operational logging.** Routine per-service messages every 2.5 seconds can generate noisy logs. Active faults are logged at `Critical`, which can incorrectly signal host failure to alerting systems; use a severity matching the operational event.
7. **Compose HTTPS inputs are external prerequisites.** Compose validation fails until certificate path/password variables are set. The local dev certificate must be trusted by the browser. Never commit a PFX or its password.
8. **Build status is unconfirmed.** Prior client builds found and fixed a nullable conditional and a Razor interpolation issue. A later build did not complete in the available session; run a clean solution build before the next release.

## Suggested first enhancement iteration

Keep the first iteration narrow and observable:

1. Add API/service tests for target/fault validation, latency bounds, activation, clearing, and case-insensitive service keys.
2. Rename the in-memory implementation to reflect its behavior, or implement actual persistence behind `IChaosOrchestrator`; do not present in-memory state as database-backed.
3. Decide the fault execution boundary. Define how `Latency`, `HttpFailure`, and `CpuSpike` affect a controlled sample dependency, and keep unsafe host-wide stress out of the web process.
4. Protect the control API with operator authentication/authorization before adding remote exposure.
5. Align server-side UI/API calls with both local launch and HTTPS Compose addresses; avoid a hard-coded localhost base URL.
6. Improve state feedback: expose last poll success/time, make injection results visible to the table immediately, and keep user-facing errors concise.
7. Build and run the host + WASM app in both local HTTP development and the configured HTTPS Compose path.

## Working conventions for the next iteration

- Preserve the three registered target keys unless the API contract and UI are changed together.
- Keep validation at the API boundary and enforce invariants in the orchestration implementation as well.
- Treat chaos injection endpoints as privileged control-plane actions.
- Avoid claiming that a fault is being executed until a real execution mechanism exists.
- Verify changes with a solution build and focused automated tests; verify Docker Compose configuration after setting the required certificate environment variables.
