# Stria Phase 1 Handoff

**Status:** Phase 0 and Phase 1 foundations are complete and pushed to `main`.

**Phase 2 plan:** See [PHASE_2_PLAN.md](PHASE_2_PLAN.md) for the authorization design, ordered work breakdown, test matrix, and exit evidence.

**Do not provision Azure resources yet.** The next roadmap step is Phase 2 (Microsoft Entra ID control-plane security). Azure infrastructure begins in Phase 3 only after explicit authorization to incur subscription cost.

## What Stria is

Stria is a .NET 10 chaos-engineering control plane. An operator uses the Blazor dashboard or versioned HTTP API to create a **bounded, time-limited** fault experiment for an allowed service. Stria stores the experiment in PostgreSQL. A participating service polls for its directive and applies the fault locally.

The current demo target is `PaymentGateway`. Its sample service can apply:

- `Latency` — delay a percentage of requests.
- `HttpFailure` — return a 5xx status for a percentage of requests.

The public API also currently recognizes `CpuSpike`, but the sample service does not execute it. Treat this as a known product limitation; Phase 5 should remove or hide it until a safe capped implementation exists.

## Safety model to explain in an interview

Stria uses defence in depth rather than relying on one validation point:

1. **Domain rules** validate target, fault type, duration, percentage, latency, and HTTP-status bounds.
2. **The orchestrator** rechecks those rules before writing an experiment.
3. **PostgreSQL** has a partial unique constraint so a target cannot have two active experiments, even under concurrent requests.
4. **Leases expire automatically.** Both the control plane and polling target treat expired directives as inactive, so a control-plane outage cannot extend a fault.
5. **Health endpoints are outside the fault path,** so the demo target remains observable while a fault is active.

This is why the central Phase 1 claim is: a target never receives an out-of-bounds directive and never has two active experiments.

## Delivered work

### Phase 0: local correctness

- Corrected Dockerfile project paths and aligned configuration names.
- Added `.env.example`; `.env` remains ignored so local passwords are not committed.
- Replaced the hard-coded server API URL with a host-aware base address.
- Added `GET /healthz` (process health) and `GET /readyz` (PostgreSQL connectivity).
- Added a persistent Compose volume for ASP.NET Core Data Protection keys.
- Documented the Compose smoke path in [README.md](../README.md).

### Phase 1: contract, testing, and CI

- Versioned the control API under `/api/v1/chaos`.
- Added RFC 7807 `application/problem+json` errors for invalid requests and unknown targets.
- Added unit tests for safety rules and defaults.
- Added PostgreSQL integration tests using Testcontainers for persistence, expiry, and concurrent active-experiment uniqueness.
- Added API tests for validation, unknown targets, `204 No Content`, expiry, inject, and clear behavior.
- Added GitHub Actions CI for restore, Release build, and test on pushes and pull requests to `main`.
- Configured `main` to require the `build-and-test` check before merge. Administrators can still bypass this rule; do not use that bypass for normal changes.

## Key locations

| Area | Location |
|---|---|
| ASP.NET Core host and API | `Stria.API/Program.cs` |
| API project | `Stria.API/Stria.API.csproj` |
| Blazor dashboard | `Stria.Client/` |
| Polling demo target | `Stria.SampleService/` |
| Domain/API/database tests | `Stria.Tests/` |
| Compose development stack | `compose.yaml` |
| CI workflow | `.github/workflows/ci.yml` |
| Product intent | `docs/STRIA_SERVICE_PLAN.md` |
| Roadmap analysis | `docs/STRIA_SYSTEM_ANALYSIS.md` |

`Stria.API` appears as a directory because it is the host project. Its assembly and root namespace intentionally remain `Stria` for compatibility with existing namespaces and the container entrypoint. Rider's solution view only shows projects included in `Stria.sln`; it now correctly includes `Stria.API/Stria.API.csproj`.

## Run and demonstrate locally

1. Copy `.env.example` to `.env` and replace the placeholder database password.
2. Run `docker compose up --build` from the repository root.
3. Confirm the control plane is healthy:

   ```sh
   curl -i http://localhost:8080/healthz
   curl -i http://localhost:8080/readyz
   ```

4. Send a bounded latency experiment:

   ```sh
   curl -i -X POST http://localhost:8080/api/v1/chaos/inject \
     -H 'Content-Type: application/json' \
     --data '{"targetService":"PaymentGateway","faultType":"Latency","latencyMs":750,"durationSeconds":120,"percent":50}'
   ```

5. Call `http://localhost:8081/work` repeatedly to see roughly half the requests delayed.
6. Clear it early, or wait for automatic expiry:

   ```sh
   curl -i -X POST http://localhost:8080/api/v1/chaos/inject \
     -H 'Content-Type: application/json' \
     --data '{"targetService":"PaymentGateway","faultType":"None"}'
   ```

7. Verify a service with no active experiment receives `204 No Content`:

   ```sh
   curl -i http://localhost:8080/api/v1/chaos/directives/PaymentGateway
   ```

For an intentional API-error demonstration, submit `latencyMs` above `60000` and point out the `400 application/problem+json` response.

## Verification evidence

The complete suite was run successfully before this handoff:

```text
dotnet test Stria.sln --configuration Release --no-restore --logger "console;verbosity=minimal"
53 passed, 0 failed, 0 skipped
```

The GitHub Actions `build-and-test` workflow also passed after the final CI update (run `43997036592`, about 1 minute 12 seconds). CI sets `STRIA_REQUIRE_DOCKER_TESTS=true`, which means it fails rather than silently skipping PostgreSQL/Testcontainers coverage when Docker is unavailable.

Run the same development checks after a source change:

```sh
dotnet restore Stria.sln
dotnet build Stria.sln --configuration Release --no-restore
dotnet test Stria.sln --configuration Release --no-build
```

## Git and working-tree note

Relevant completed commits:

- `a7e024d` — Phase 0 and Phase 1 foundations.
- `122b2f9` — CI action runtime update.

At the time of handoff, local `Stria.Client/bin`, `Stria.Client/obj`, `Stria.SampleService/bin`, and `Stria.SampleService/obj` files may be modified by Rider or local builds. They are generated artifacts, were intentionally not included in the implementation commits, and should not be mixed into feature commits.

## Next work: Phase 2

Phase 2 makes fault injection authorized and auditable before public Azure ingress exists. The full ordered plan, security design, and test matrix are in [PHASE_2_PLAN.md](PHASE_2_PLAN.md). The first step is CI hardening, followed by:

1. Register the Microsoft Entra application and define `ChaosViewer`, `ChaosOperator`, and `ChaosAdmin` app roles.
2. Add Microsoft.Identity.Web with OIDC cookies for the Blazor host and JWT bearer support for API clients.
3. Authorize read versus mutation routes; protect the UI with `AuthorizeRouteView`.
4. Derive `TriggeredBy` from stable Entra claims (`oid`, `sub`, or `NameIdentifier`), never from client JSON.
5. Add request-size limits and rate limits.
6. Separate data-plane authentication for directive polling from browser authentication. Start with a Key Vault-backed service token in a later Azure environment; do not reuse operator OIDC tokens.
7. Add and test an admin-only global kill switch that clears or suppresses active directives.

Phase 2 exit evidence: anonymous callers cannot inject or clear; viewers cannot mutate; every state change has a trusted actor; the data-plane cannot read directives without its own credential.

## Short interview summary

> I built Stria as a .NET chaos-control-plane demo. Its key engineering constraint is safety: experiments are bounded, leased, validated in the domain and orchestration layers, and protected by a PostgreSQL uniqueness rule under concurrency. I tested those behaviors at unit, API, and real PostgreSQL integration levels using Testcontainers. I then put those checks behind a required GitHub Actions build-and-test gate. The next step is Entra role-based authorization so only authorized operators can create or clear faults before I deploy the control plane to Azure Container Apps.
