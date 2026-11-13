# Stria Service Plan

## Product goal

Turn Stria into a deployable, internal resilience-testing platform for containerized .NET services. It will let authorized operators run safe, time-bounded fault experiments against explicitly enrolled non-production services and verify that those services received and applied the experiment.

The first public-facing outcome is a recruiter-safe demo deployment: a hosted dashboard, a private sample target, a visible experiment lifecycle, and reproducible source/deployment instructions.

## Scope and safety boundary

### In scope

- Controlled latency and HTTP 5xx fault experiments.
- Authenticated operator control of enrolled demo or staging services.
- Explicit experiment duration, percentage, target capability, audit history, and automatic expiry.
- A reusable .NET data-plane integration for services that opt in.
- PostgreSQL-backed state, observability, automated tests, CI, and container deployment.

### Out of scope for the first deployable release

- Faulting arbitrary URLs, hosts, or services.
- Unbounded CPU, memory, disk, or network exhaustion.
- Production fault injection by default.
- Multi-region control-plane high availability.
- A general-purpose service mesh or traffic proxy.

## Target architecture

```text
Authenticated operator
  └── Stria dashboard / versioned control API
        ├── experiment, service-registry, audit, and acknowledgement records
        ├── PostgreSQL
        ├── metrics, traces, and structured logs
        └── authenticated directive API
              └── enrolled target service + Stria .NET integration
                    ├── polls or receives a bounded directive
                    ├── applies supported fault locally
                    └── reports received / applied / rejected / expired status
```

The control plane issues directives; it never performs disruptive work inside its own process. Target services apply only fault types they explicitly advertise and only within their configured caps.

## Delivery milestones

### Milestone 1 — Reliable local baseline

**Objective:** Make the current demo repeatable and verifiable from a clean checkout.

Work:

- Keep Compose ports and documentation aligned (`Stria`: `localhost:8080`; sample service: `localhost:8081`).
- Add a checked-in `.env.example` with placeholders only.
- Add `GET /healthz` and `GET /readyz` to Stria; readiness must validate database connectivity.
- Add configuration validation for database, expiry, retention, and sample-service settings.
- Remove or replace `TrafficSimulatorService`, since it generates log noise but does not simulate traffic.
- Replace the hard-coded server-side API base URL with a host-aware approach that works locally and behind HTTPS ingress.
- Document a smoke-test sequence: start stack, inject latency, observe `/work`, clear, and confirm expiry.

**Acceptance criteria:** A fresh environment can bring up the stack and demonstrate a latency and a 503 fault without undocumented setup.

### Milestone 2 — Test and contract foundation

**Objective:** Prove the core safety rules before expanding features.

Work:

- Add unit tests for `ChaosRules`, fault defaults, duration bounds, percentage bounds, and clear semantics.
- Add PostgreSQL integration tests for persistence, active-experiment uniqueness, replacement, expiry, and retention.
- Add API tests for success, invalid input, unknown targets, `204` directives, and expired leases.
- Define versioned API routes under `/api/v1` and return RFC-style problem details for errors.
- Add idempotency keys for create and clear commands.

**Acceptance criteria:** CI can prove that a target never receives an out-of-bounds directive and never has two active experiments.

### Milestone 3 — Secure control plane

**Objective:** Make fault injection an authorized operation.

Work:

- Integrate OpenID Connect with a configurable identity provider.
- Define `ChaosViewer` and `ChaosOperator` authorization policies.
- Require an authenticated operator for create, clear, and service-management actions.
- Derive actor identity from stable claims; do not trust request-supplied audit fields.
- Add rate limits, request-size limits, secure CORS configuration, and security-focused logs.
- Add an administrator-only global kill switch that clears or suppresses all active directives.

**Acceptance criteria:** anonymous requests cannot read directives or modify experiments; every state change has a trusted actor and audit record.

### Milestone 4 — Service registry and experiment lifecycle

**Objective:** Replace hard-coded targets with safe, explicit service enrollment.

Work:

- Add a service registry with service ID, display name, environment, supported fault types, per-fault limits, and status.
- Associate experiments with a registered service and environment rather than a free-form target name.
- Add experiment rationale/ticket reference, requested/effective expiry, lifecycle events, and cancellation reason.
- Enforce per-service constraints such as maximum latency, maximum duration, and permitted percentage.
- Add experiment list/detail/history endpoints and filtered dashboard views.

**Acceptance criteria:** Stria rejects faults a service did not enroll for and an operator can trace every experiment from request through end state.

### Milestone 5 — Supported data-plane integration

**Objective:** Make the sample integration reusable and observable.

Work:

- Extract directive polling, local leases, and safe fault middleware into a versioned `Stria` .NET package.
- Authenticate target services with workload credentials or service-specific tokens.
- Preserve the current fail-safe behavior: a control-plane outage cannot extend an existing directive beyond its local lease.
- Add target acknowledgements: `Received`, `Applied`, `Rejected`, `Expired`, and fault-specific detail.
- Keep health endpoints outside the fault middleware.
- Support only `Latency` and `HttpFailure` in the first release; remove `CpuSpike` from public controls until a safe design exists.

**Acceptance criteria:** the dashboard can distinguish an experiment that was requested from one that a target actually applied.

### Milestone 6 — Operator experience and observability

**Objective:** Give operators enough evidence to run and explain an experiment safely.

Work:

- Display duration, remaining lease, percentage, status code, actor, target acknowledgement, and lifecycle history.
- Add service/experiment filters and concise operator-safe error messages.
- Emit OpenTelemetry traces, metrics, and structured logs.
- Track active experiments, control command outcomes, directive poll failures, acknowledgement latency, and rejected directives.
- Add alerts for database failures, unreachable targets, failed directive application, and kill-switch activation.

**Acceptance criteria:** during an active experiment, an operator can identify who initiated it, what it affects, whether it is applied, and when it will stop.

### Milestone 7 — Delivery pipeline and hosted demo

**Objective:** Produce an independently deployable portfolio release.

Work:

- Add GitHub Actions to restore, build, test, build images, scan dependencies/images, and publish immutable images.
- Tag images with a release version and Git revision; do not deploy mutable `latest` tags.
- Add infrastructure-as-code for an Azure Container Apps deployment plus managed PostgreSQL.
- Run migrations as a controlled release job rather than application startup in hosted environments.
- Expose only the dashboard/control plane behind HTTPS and authentication; keep target services and PostgreSQL private.
- Deploy a demo `PaymentGateway` that applies latency and HTTP failures, and add a visible request/result panel.
- Create a recruiter-facing README, architecture diagram, screenshots, and a 2–3 minute recorded walkthrough.

**Acceptance criteria:** a reviewer can open the demo URL, observe a safe experiment lifecycle, inspect the source, and reproduce the deployment path from documentation.

## Release gates

Do not promote Stria beyond a local demo until each applicable gate is met:

| Gate | Required evidence |
|---|---|
| Functional correctness | Unit, API, and PostgreSQL integration tests pass |
| Fault safety | Lease expiry, global kill switch, capability limits, and target-side caps are tested |
| Access control | Unauthenticated and unauthorized control/data-plane requests are rejected |
| Observability | Health/readiness, logs, metrics, directive status, and target acknowledgements are visible |
| Delivery | Immutable image, database migration plan, rollback procedure, and deployment smoke test exist |
| Demo safety | Public link exposes no unrestricted fault injection and targets only owned demo services |

## Prioritized first release

The smallest coherent deployable release is Milestones 1–3 plus the `Latency` and `HttpFailure` portions of Milestone 5, followed by the hosted-demo work in Milestone 7. Milestones 4 and 6 deepen the platform and should follow immediately if Stria is intended for use beyond a portfolio demonstration.

## Definition of done

Stria is ready for an internal pilot when an authenticated operator can choose an enrolled non-production service, create a bounded latency or HTTP-failure experiment, see that target acknowledge application, observe the result, and rely on automatic expiry or an emergency kill switch to stop it. The entire flow must be tested, auditable, observable, reproducibly deployed, and reversible.
