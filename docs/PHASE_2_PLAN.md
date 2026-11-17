# Stria Phase 2 Plan — Authorized Control Plane

**Status:** In progress. Step 0 (CI and quality gates) is implemented locally and awaiting its first GitHub Actions run.

**Scope guard:** Phase 2 is a security/identity phase. Do not provision Azure resources or
incur subscription cost. Use a test Entra tenant or a local OIDC provider for development
until Phase 3 is explicitly authorized.

## 1. Objective

Make fault injection an authorized, auditable operation before Stria is exposed through public
ingress. Phase 2 is complete when all of the following are true:

- Anonymous callers cannot inject, clear, read control-plane state, or read directives.
- `ChaosViewer` users can read experiments but cannot mutate them.
- `ChaosOperator` users can create and clear bounded experiments.
- `ChaosAdmin` users can operate the global kill switch.
- Every state change records a trusted actor derived from identity claims, never from request JSON.
- The data plane authenticates with its own service credential and can read only its own directive.
- Mutation endpoints have request-size and rate limits, and cookie-authenticated mutations are
  protected against CSRF.
- CI proves the authorization matrix, kill-switch behavior, and data-plane isolation.

This is the exit evidence described in
[PHASE_1_HANDOFF.md](PHASE_1_HANDOFF.md#next-work-phase-2).

## 2. Current baseline

Phase 0 and Phase 1 are complete:

- Versioned control API at `/api/v1/chaos`.
- RFC 7807 problem details for invalid requests and unknown targets.
- Domain, API, and PostgreSQL/Testcontainers tests (53 passing at handoff).
- GitHub Actions `build-and-test` gate required on `main`.
- Compose stack with a polling `PaymentGateway` data-plane sample.

Known gaps relevant to Phase 2:

- All control and directive endpoints are anonymous.
- `TriggeredBy` is recorded as `anonymous`; the request field is ignored but there is no trusted actor.
- The directive endpoint has no data-plane authentication.
- No CSRF protection for a future cookie-authenticated dashboard.
- No request-size or rate limits.
- No global kill switch.
- `CpuSpike` is still exposed in the UI/API but is not executed by the sample service. Do not
  expand it in Phase 2; Phase 5 removes or hides it.
- No authorization or negative-security tests in CI.

The dashboard is a hosted Blazor WebAssembly app that calls the control API same-origin. The
sample service polls the directive API. Those two callers have different trust models and must
not share credentials.

### 2.1 Review findings still open from Phase 1

These are not new Phase 2 features, but they should be handled while the control plane is being
secured:

- `TrafficSimulatorService` is still registered as a hosted service and logs active faults at
  `Critical`. Remove or replace it so routine fault state does not create alert noise.
- The dashboard prints raw exception text (`ex.Message`) to operators. Replace it with concise
  operator-safe messages and keep diagnostics in structured logs.
- `CpuSpike` remains visible in the dashboard and accepted by the API even though the sample
  service does not execute it. Hide it from operator controls in Phase 2; remove it fully in
  Phase 5 unless a capped implementation exists.
- Health and readiness endpoints remain anonymous by design, but must not expose configuration
  or exception details.
- Compose still migrates on startup for local convenience. Keep that behavior scoped to local
  development; Phase 3 uses a controlled release migration.

## 3. Security design

### 3.1 Identity provider and roles

Use Microsoft Entra ID as the identity provider.

| App role | Capability |
|---|---|
| `ChaosViewer` | Read active experiments and experiment history; open the dashboard |
| `ChaosOperator` | Everything a viewer can do, plus inject and clear bounded experiments |
| `ChaosAdmin` | Everything an operator can do, plus engage/release the global kill switch |

Define three app roles rather than overloading `ChaosOperator` for administration. Policies accept
role membership as follows:

| Policy | Accepted roles |
|---|---|
| `Chaos.View` | `ChaosViewer`, `ChaosOperator`, `ChaosAdmin` |
| `Chaos.Operate` | `ChaosOperator`, `ChaosAdmin` |
| `Chaos.Admin` | `ChaosAdmin` |

Entra app roles are assigned through enterprise-app assignments or group assignments. Do not
use display names or email prefixes for authorization.

### 3.2 Browser authentication

Use Microsoft.Identity.Web with OpenID Connect authorization-code flow and an encrypted,
`HttpOnly`, `Secure`, `SameSite=Lax` cookie for the Blazor host. The WASM dashboard calls the API
same-origin, so the browser sends the cookie automatically.

Because cookie authentication is ambient, every cookie-authenticated mutation must require an
antiforgery token. The dashboard will fetch a token and send it in the configured header for
`POST` requests. External API clients use bearer tokens and are not CSRF-susceptible.

Do not enable wildcard CORS. The dashboard is same-origin; add explicit origins only if a separate
hosted dashboard is introduced later.

### 3.3 API client authentication

Add JWT bearer authentication for automation and future CLI/API clients:

- Expose an API scope such as `api://stria-control/Chaos.Access`.
- Validate issuer, audience, signature, lifetime, and the `roles` claim.
- Accept only tokens issued for the Stria control-plane audience.
- Never accept data-plane tokens for control-plane routes.

The two authentication schemes must be explicit on endpoint groups. Avoid a permissive default
scheme that lets one credential type satisfy the other's policy.

### 3.4 Data-plane authentication

The directive endpoint must not accept operator cookies or operator bearer tokens. Give the data
plane a separate scheme and credential:

- Each enrolled service has its own credential, not one shared global secret.
- A service may read only `/directives/{its-own-service-name}`.
- Local development uses a development-only configuration secret.
- Azure uses a Key Vault-backed secret or workload identity in Phase 3; it is not committed.
- Prefer a signed JWT with a service subject and audience, or a hashed per-service API key with
  constant-time comparison. Phase 2 can start with per-service hashed keys if Entra workload
  identities are not available locally.

Update `Stria.SampleService` to send the credential. Add negative tests before enabling the
credential in Compose.

### 3.5 Trusted audit identity

Derive actor identity on the server:

- Prefer the Entra `oid` claim as the stable object identifier.
- Fall back to `sub`/`NameIdentifier` when `oid` is unavailable.
- Store the stable identifier in `TriggeredBy`/`ClearedBy`.
- Add a nullable display-name field for operator UI convenience; never use it for authorization.
- Use `system:expiry` and `system:kill-switch` for system actions.
- Reject or ignore any client-supplied actor field. Keep the request field for wire compatibility
  only if needed, but document that it is ignored.

Add a migration for any new audit columns and cover it with PostgreSQL integration tests.

### 3.6 Limits and hardening

- Request body limit: small JSON cap (for example 16 KB) on `/api/v1/chaos/*`.
- Rate limits: per-user/IP read and mutation policies; a separate data-plane polling policy keyed
  by service identity.
- Return `429` with `Retry-After` and problem details.
- Security headers: HSTS in non-development, `X-Content-Type-Options: nosniff`,
  `Referrer-Policy`, `Permissions-Policy`, and a content security policy compatible with Blazor.
- Forwarded headers only when a trusted proxy is configured; do not trust arbitrary
  `X-Forwarded-*` headers from the internet.
- Log authentication failures, authorization denials, rate-limit rejections, and kill-switch
  actions without logging tokens, cookies, or secrets.

### 3.7 Global kill switch

Add a persistent kill-switch record and an admin-only endpoint pair:

- `POST /api/v1/chaos/kill-switch/engage` — clears all active experiments and suppresses new
  injections until released.
- `POST /api/v1/chaos/kill-switch/release` — allows injections again.
- `GET /api/v1/chaos/kill-switch` — returns current state to viewers.

The orchestrator must check the kill switch before writing an experiment, not only in the HTTP
handler. A kill-switch state change is auditable with actor, timestamp, and reason. The dashboard
shows a persistent banner while it is engaged.

## 4. Work breakdown

### Step 0 — CI and quality gates (current)

- Add cached NuGet restore. Direct package versions are exact in the project files; repository-wide
  lock files are intentionally deferred because SDK implicit packages change between .NET SDK
  patches, which makes locked restore brittle across local, CI, and container SDK images.
- Add a formatting gate, warnings-as-errors build, PostgreSQL/Testcontainers tests, coverage
  collection, and test-result artifacts.
- Add a NuGet vulnerability audit for direct and transitive packages.
- Add pull-request dependency review for newly introduced vulnerable dependencies.
- Build the control-plane, dashboard, and sample-service container images on every change.
- Add Dependabot updates for NuGet, GitHub Actions, and Docker base images.
- Stop tracking generated `bin/`/`obj/` artifacts so clean checkouts stay clean.
- Record the required-check names in the README.

**Acceptance:** CI is green on an unchanged tree; a formatting violation, a vulnerable package,
or a broken Dockerfile fails the pipeline. The auth tests added in later steps plug into the same
`build-and-test` job.

### Step 1 — Identity foundation and local auth harness

- Register the Entra application(s) in a test tenant and define the three app roles, redirect URIs,
  API scope, and test group assignments.
- Add Microsoft.Identity.Web to the host and configuration validation that fails fast when
  required identity settings are missing outside Development.
- Add OIDC cookie sign-in/out for the browser and JWT bearer validation for API clients.
- Add a test authentication handler or signed test JWTs for integration tests. Never ship an
  "authentication disabled" switch that can be enabled in a deployed environment.
- Document local setup with user secrets; commit only placeholder configuration and `.env.example`.

**Acceptance:** an unauthenticated request is challenged; a valid test token creates an
authenticated principal; no secret is committed.

### Step 2 — Route authorization and trusted actors

- Apply `Chaos.View`, `Chaos.Operate`, and `Chaos.Admin` policies to the API route groups.
- Protect the dashboard with `AuthorizeRouteView` and a sign-in prompt; show the signed-in user.
- Derive `TriggeredBy`/`ClearedBy` from `oid`/`sub`; add display-name audit data.
- Add tests for anonymous, viewer, operator, admin, wrong-audience, and expired-token cases.
- Add a spoofed-`TriggeredBy` test proving the JSON field is ignored.

**Acceptance:** the authorization matrix is enforced and every mutation has a stable actor ID.

### Step 3 — CSRF, limits, and security headers

- Require antiforgery tokens for cookie-authenticated mutations.
- Add request-size limits, rate limits, and problem-details responses.
- Add security headers and explicit same-origin/CORS behavior.
- Replace raw dashboard exception messages with operator-safe errors; keep diagnostics in logs.
- Add tests for missing antiforgery tokens, oversized bodies, and `429` responses.

**Acceptance:** cookie-authenticated mutations cannot be forged cross-site, and abuse limits are
observable and tested.

### Step 4 — Data-plane credential separation

- Add a dedicated data-plane authentication scheme and per-service credentials.
- Bind the credential to the requested service name; reject cross-service reads.
- Update the sample service and Compose configuration.
- Add a data-plane test category and CI coverage.

**Acceptance:** control-plane credentials cannot read directives; a service cannot read another
service's directive; the sample service still applies latency and HTTP failures.

### Step 5 — Admin kill switch

- Add persistence, orchestrator enforcement, admin endpoints, and dashboard state.
- Clear active experiments on engage and suppress new injections.
- Add tests for non-admin denial, clearing, suppression, release, and expiry interaction.

**Acceptance:** an admin can stop all fault injection immediately, and no operator or data-plane
credential can engage or release the switch.

### Step 6 — Verification and handoff

- Run the full test suite and CI; add an authenticated Compose smoke test.
- Update the README, Phase 2 handoff, threat model, and evidence table.
- Confirm branch protection requires the final CI checks.
- Review logs and error responses for secret leakage.

**Acceptance:** the Phase 2 exit evidence is reproducible from a clean checkout.

## 5. Test matrix

| Caller | `GET /faults` | `POST /inject` | `GET /directives/{service}` | Kill switch |
|---|---|---|---|---|
| Anonymous | `401` | `401` | `401` | `401` |
| `ChaosViewer` | `200` | `403` | `403` | `403` |
| `ChaosOperator` | `200` | `202` | `403` | `403` |
| `ChaosAdmin` | `200` | `202` | `403` | `202`/`200` |
| Data plane for its own service | `403` | `403` | `200`/`204` | `403` |
| Data plane for another service | `403` | `403` | `403` | `403` |
| Expired/wrong-audience JWT | `401` | `401` | `401` | `401` |

Add separate cases for rate limits (`429`), oversized bodies (`413`), missing antiforgery tokens
(`400`), and kill-switch suppression (`409`).

## 6. Risks and decisions

| Risk / decision | Mitigation |
|---|---|
| Cookie-auth plus Blazor WASM makes CSRF protection necessary | Require antiforgery tokens for cookie mutations; use bearer tokens for automation |
| Entra tenant/app registration is external setup | Do identity setup in a test tenant; keep integration tests local with a test handler |
| Testcontainers plus auth can make tests slow/flaky | Reuse one container per class, use a signed test token issuer, keep auth tests in the existing integration job |
| Data-plane credentials could be confused with operator tokens | Separate schemes, audiences, and policies; add cross-credential negative tests |
| Migrations add risk to a live schema | Forward-only EF migration; test on PostgreSQL in CI; keep startup migration for local Compose only |
| `CpuSpike` remains visible but unexecutable | Do not expand it in Phase 2; remove/hide it in Phase 5 |
| No Azure spend in Phase 2 | Test tenant/local OIDC only; Phase 3 infra requires explicit authorization |

## 7. Definition of done

- [ ] CI fails on formatting, build warnings, vulnerable packages, or broken images.
- [ ] Anonymous control-plane and directive requests are rejected.
- [ ] Viewers cannot mutate; operators cannot administer the kill switch.
- [ ] Actor IDs come from validated claims and are covered by tests.
- [ ] Data-plane credentials are service-scoped and cannot be used on control routes.
- [ ] Rate limits, request-size limits, and CSRF protection are tested.
- [ ] Kill switch clears and suppresses faults, with audit history.
- [ ] README and handoff document the auth setup, required CI checks, and Phase 2 evidence.
