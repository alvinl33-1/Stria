# Stria Phase 2 Handoff

**As of:** 2026-11-18. **Status:** Phase 2 is complete. Every step in
[PHASE_2_PLAN.md](PHASE_2_PLAN.md) is merged: PR #14 (Steps 0–3), PR #15 (Step 4), PR #20 (Step 5),
PR #21 (Step 6). `main` requires `build-and-test`, `dependency-audit`, `dependency-review` and
`container-build`. **Next:** Phase 3, deployment to Azure Container Apps; see
[PHASE_3_PLAN.md](PHASE_3_PLAN.md). Its first three steps need no Azure account; provisioning
needs explicit authorization because it creates paid resources.

**Scope guard (held):** Phase 2 created no Azure resources. Entra work used the free test tenant.

## Start here next session

1. `git switch main && git pull`, then a new branch for the work.
2. Confirm `.env` has every variable in [`.env.example`](../.env.example). It is gitignored and
   persists locally.
3. `docker compose up --build -d`, then `scripts/compose-smoke.sh`. Expect `All checks passed.`
4. Run the gates:
   ```sh
   dotnet format Stria.sln --verify-no-changes
   dotnet build Stria.sln -c Release -warnaserror
   STRIA_REQUIRE_DOCKER_TESTS=true dotnet test Stria.sln -c Release --no-build
   ```
   Expected: clean, 0 warnings, 119 passed / 0 skipped.

## What is built

| Step | Delivered | Main locations |
|---|---|---|
| 0 | CI: format, warnings-as-errors build, tests with PostgreSQL, NuGet audit, dependency review, container builds | `.github/workflows/ci.yml` |
| 1 | Entra sign-in: OIDC cookie for the dashboard, JWT bearer for automation; Production refuses to start without identity settings | `Stria.API/Program.cs`, [ENTRA_SETUP.md](ENTRA_SETUP.md) |
| 2 | `Chaos.View` / `Chaos.Operate` / `Chaos.Admin` policies; audit actor from validated `oid` + display name | `Stria.Client/Security/ChaosPolicies.cs`, `Stria.API/Security/ActorResolver.cs` |
| 3 | Antiforgery on cookie POSTs, 16 KB body cap, per-user rate limits, security headers, operator-safe errors | `Stria.API/Security/HttpHardening.cs`, `CookieAntiforgeryFilter.cs` |
| 4 | Per-service data-plane keys (`X-Stria-Service-Key`, SHA-256 hashes, constant-time compare), own-directive policy, per-service polling limit | `Stria.API/Security/DataPlaneAuthentication.cs`, `Stria.SampleService/` |
| 5 | Admin kill switch: append-only `KillSwitchEvents` audit, enforcement in the orchestrator under an advisory lock, `409` while engaged, dashboard banner and admin controls | `Stria.API/Services/DatabaseChaosOrchestrator.cs`, `Stria.Client/Components/KillSwitchPanel.razor` |
| 6 | Enforced CSP with a per-request nonce; CI starts the API image and fetches its Blazor script; `scripts/compose-smoke.sh`; secret-leak review; `.idea/` untracked | `HttpHardening.cs`, `Components/App.razor`, `ci.yml`, `scripts/` |

## Phase 2 exit evidence

Each objective in [PHASE_2_PLAN.md §1](PHASE_2_PLAN.md#1-objective), and where it is proven.

| Objective | Automated evidence | Hand-checked on 2026-11-18 |
|---|---|---|
| Anonymous callers can't inject, clear, read state or read directives | `ApiSecurityTests`, `KillSwitchApiTests`, `DataPlaneSecurityTests` | `compose-smoke.sh`: `401` on all 6 control and directive routes |
| Viewers read but can't mutate | `ApiSecurityTests` (`403` on inject), `KillSwitchApiTests` (`403` on engage/release) | Earlier steps (test3) |
| Operators inject and clear bounded experiments | `ApiSecurityTests`, `ChaosApiTests` | Admin injected latency: `/work` 9 ms → 3.03 s |
| Admins operate the kill switch; operators can't | `KillSwitchApiTests`, `KillSwitchOrchestratorTests` (incl. engage-vs-inject race) | Engage cleared the fault (`/work` back to 9 ms, row `ClearedBy = system:kill-switch`); banner shown; inject disabled; release re-enabled injection (3.01 s) |
| Actor comes from claims, never JSON | Spoofed-actor tests for inject and engage | Audit rows show the signed-in name |
| Data plane uses its own credential, own directive only | `DataPlaneSecurityTests` | `compose-smoke.sh`: own `204`, other `403`, key on control routes `401` |
| Size limits, rate limits, CSRF | `HardeningTests`, `RateLimitTests` | — |
| CI proves the matrix, kill switch and data-plane isolation | PR checks: `build-and-test`, `container-build`, `dependency-audit`, `dependency-review`, CodeQL | — |
| Browser hardening | `HardeningTests` (enforced CSP, nonce on the import map, nonce per request) | Dashboard under the enforced CSP: no console messages through inject, engage, release |
| No secrets in logs or error bodies | — | Compose logs (default level, then Debug auth logging) contain none of the DB password, client secret, data-plane key or its hash, and no rejected key. Error bodies are empty or generic; `/readyz` with the DB down is `503` with a trace ID only |

**Sign-out under the enforced CSP** (checked after PR #21 merged): the form post reaches Entra's
logout page, and the next request to `/` gets `302` to sign-in, so Stria's cookie is gone. A
personal Microsoft account is then signed back in silently because Microsoft keeps its own session;
use a private window to switch accounts.

## Threat model: what Phase 2 leaves for later

| Risk | Status | Owner |
|---|---|---|
| Data-plane keys and session cookies cross plain HTTP in Compose | Accepted for local use only | Phase 3: TLS at ingress |
| Static data-plane keys, no rotation | Accepted locally | Phase 3: Key Vault, or Entra workload identity for services |
| Data Protection keys in a Docker volume | Local only | Phase 3: Blob storage + Key Vault |
| Rate limits are in memory, per instance | Fine for one instance | Phase 3, if it scales out |
| `style-src 'unsafe-inline'` (status bar widths) | Low risk; scripts are strict | Could move widths to CSS classes |
| Admins can bypass branch protection | `enforce_admins` is off | Decide when there are other contributors |
| Kill-switch changes reach dashboards by polling (≤ 3 s) | Enforcement is immediate server-side; only the display lags | — |
| `CpuSpike` accepted by the API but not executed | Hidden in the dashboard | Phase 5 |

## Local environment state

- **Entra test tenant:** app registration "Stria (local)" with app roles `ChaosViewer`,
  `ChaosOperator` and `ChaosAdmin`. **Assignment required** is on. Redirect URIs include
  `http://localhost:8080/signin-oidc`.
- **Test accounts (one role each):**

  | Account | Role |
  |---|---|
  | Alvin Lee (personal Microsoft account) | `ChaosAdmin` |
  | alvinlee (tenant-native account) | `ChaosAdmin` |
  | test1 | `ChaosOperator` |
  | test3 | `ChaosViewer` |

- **Secrets:** `.env` holds the database password, the Entra client secret and the PaymentGateway
  data-plane key and hash. Nothing secret is committed.
- **Role changes** take effect only after signing out of Stria and back in; roles live in the
  session cookie.

## Lessons from this phase

- **Test auth can hide real auth bugs.** Microsoft.Identity.Web swaps the OIDC token handler, so
  `MapInboundClaims = false` never reached it and every browser user was denied. `ClaimMappingTests`
  now covers the real handlers.
- **The Docker image can differ from the local build.** A restore-before-copy Dockerfile dropped
  `_framework/blazor.web.js`; fixed with `RequiresAspNetWebAssets=true`. CI's `container-build`
  now starts the image and fetches the script, so this fails CI.
- **Static assets are endpoints in .NET 10.** Use `MapStaticAssets().AllowAnonymous()`, not
  `UseStaticFiles()`.
- **macOS hides timestamp precision bugs.** Linux clocks carry 100 ns ticks; PostgreSQL stores
  microseconds. A value returned before saving differed from the stored one only in CI. Truncate
  to microseconds before persisting anything a caller compares.
- **Blazor's `<ImportMap>` is an inline script.** An enforced `script-src 'self'` blocks it; pass a
  per-request nonce (hex, so Razor's HTML encoding leaves it alone).
- **Read Committed is not enough for a global gate.** Checking the kill switch inside the injection
  transaction still lets an injection commit after a concurrent engage; the advisory lock closes
  that gap.
- **Pushing to a merged PR's branch does nothing visible.** Open a new PR or start a new branch.
- **Diagnosing "Access denied":** set
  `Logging__LogLevel__Microsoft.AspNetCore.Authorization=Debug` on the Stria container; see
  [ENTRA_SETUP.md](ENTRA_SETUP.md#5-troubleshooting). Doing so logs no secrets (checked above).

## Next: Phase 3

[PHASE_3_PLAN.md](PHASE_3_PLAN.md) has the objective, Compose-to-Azure mapping, work breakdown and
the decisions needed before provisioning. Start with Step 1 (cloud-ready code, no Azure needed).
The repository skills `compose-to-container-apps`, `dotnet-azure-runtime` and `azd-deployment`
cover the details. The threat-model rows owned by Phase 3 above are its inputs.
