# Stria Phase 2 Handoff (in progress)

**As of:** 2026-11-18. **Status:** Steps 0–4 of [PHASE_2_PLAN.md](PHASE_2_PLAN.md) are merged to
`main` (PR #14: Steps 0–3; PR #15: Step 4 and a dashboard asset fix). **Next:** Step 5, the admin
kill switch.

**Scope guard (unchanged):** no Azure resources in Phase 2. Entra work uses the free test tenant.

## Start here next session

1. Start from an up-to-date `main` and a new branch: `git switch main && git pull && git switch -c phase-2/kill-switch`.
2. Confirm `.env` has all six variables in [`.env.example`](../.env.example). It is gitignored and
   persists locally.
3. `docker compose up --build -d`, sign in at http://localhost:8080 as the admin test account, and
   check the dashboard shows three **Healthy** services.
4. Run the gates before changing anything:
   ```sh
   dotnet format Stria.sln --verify-no-changes
   dotnet build Stria.sln -c Release -warnaserror
   STRIA_REQUIRE_DOCKER_TESTS=true dotnet test Stria.sln -c Release --no-build
   ```
   Expected: clean, 0 warnings, 88 passed / 0 skipped.

## What is built

| Step | Delivered | Main locations |
|---|---|---|
| 0 | CI: format, warnings-as-errors build, tests with PostgreSQL, NuGet audit, dependency review, container builds | `.github/workflows/ci.yml` |
| 1 | Entra sign-in: OIDC cookie for the dashboard, JWT bearer for automation; Production refuses to start without identity settings | `Stria.API/Program.cs`, [ENTRA_SETUP.md](ENTRA_SETUP.md) |
| 2 | `Chaos.View` / `Chaos.Operate` / `Chaos.Admin` policies; audit actor from validated `oid` + display name | `Stria.Client/Security/ChaosPolicies.cs`, `Stria.API/Security/ActorResolver.cs` |
| 3 | Antiforgery on cookie POSTs, 16 KB body cap, per-user rate limits, security headers (CSP report-only), operator-safe errors | `Stria.API/Security/HttpHardening.cs`, `CookieAntiforgeryFilter.cs` |
| 4 | Per-service data-plane keys (`X-Stria-Service-Key`, SHA-256 hashes, constant-time compare), own-directive policy, per-service polling limit | `Stria.API/Security/DataPlaneAuthentication.cs`, `Stria.SampleService/` |

`Chaos.Admin` is defined but not yet used by any endpoint; Step 5 is its first consumer.

## Local environment state

- **Entra test tenant:** app registration "Stria (local)" with app roles `ChaosViewer`,
  `ChaosOperator` and `ChaosAdmin`. **Assignment required** is on. Redirect URIs include
  `http://localhost:8080/signin-oidc`.
- **Test accounts (one role each):**

  | Account | Role | Use for |
  |---|---|---|
  | Alvin Lee (personal Microsoft account) | `ChaosAdmin` | Kill-switch work in Step 5 |
  | alvinlee (tenant-native account) | `ChaosAdmin` | Second admin |
  | test1 | `ChaosOperator` | Inject/clear; must be denied the kill switch |
  | test3 | `ChaosViewer` | Read-only; injection returns `403` |

- **Secrets:** `.env` holds the database password, the Entra client secret and the PaymentGateway
  data-plane key and hash. Nothing secret is committed.
- **Role changes** take effect only after signing out of Stria and back in; roles live in the
  session cookie.

## Verification evidence

- PR #15 CI: `build-and-test`, `container-build`, `dependency-audit` and `dependency-review` all
  passed at `b07c0a0`. 88 tests passed, 0 skipped.
- Manual, in Compose: sign-in as `ChaosAdmin` reaches the dashboard with no console errors; the
  sample service polls with its key and receives `204`.
- **Not yet re-verified by hand after Step 4:** injecting latency from the dashboard and observing
  `http://localhost:8081/work` slow down. Do this first in the next session. Step 6 also needs it.

## Lessons from this phase

These cost real debugging time; keep them in mind for Steps 5–6.

- **Test auth can hide real auth bugs.** Microsoft.Identity.Web swaps the OIDC token handler, so
  `MapInboundClaims = false` never reached it and every browser user was denied. The cookie tests
  used a test handler and missed it. `ClaimMappingTests` now covers the real handlers.
- **The Docker image can differ from the local build.** The Dockerfile restores before `.razor`
  files are copied, so the SDK skipped the package that provides `_framework/blazor.web.js`.
  Fixed with `RequiresAspNetWebAssets=true` in `Stria.API.csproj`. `StaticAssetsTests` runs
  against a local build and cannot catch this class of problem; see the follow-ups below.
- **Static assets are endpoints in .NET 10.** Use `MapStaticAssets().AllowAnonymous()`, not
  `UseStaticFiles()`, or `@Assets[...]` paths break and the fallback sign-in policy blocks CSS/JS.
- **Pushing to a merged PR's branch does nothing visible.** GitHub doesn't update or run CI for a
  closed PR. After a merge, open a new PR or start a new branch.
- **Diagnosing "Access denied":** set
  `Logging__LogLevel__Microsoft.AspNetCore.Authorization=Debug` on the Stria container; see
  [ENTRA_SETUP.md](ENTRA_SETUP.md#5-troubleshooting).

## Next: Step 5, admin kill switch

The spec is plan section 3.7 and Step 5. Code it will touch:

- **Persistence:** a new entity in `Stria.API/Domain/` and a `DbSet` in `StriaDbContext`, plus a
  migration. Because the audit needs actor, timestamp and reason, an append-only
  `KillSwitchEvents` table (current state = latest event) fits better than a single mutable row.
  Decide this first.
- **Orchestrator:** add engage, release and get-state methods to `IChaosOrchestrator`. Enforce the
  switch inside `DatabaseChaosOrchestrator.InjectFaultAsync`, not only in the HTTP handler. Check
  it in the same transaction as the write, so an injection cannot slip in during an engage.
  Engaging clears active experiments with the actor `system:kill-switch`, matching the existing
  `ExpiryActor = "system:expiry"`.
- **Endpoints:** `POST /api/v1/chaos/kill-switch/engage` and `/release` with
  `ControlPlane(ChaosPolicies.Admin)`, the mutate rate limit and `CookieAntiforgeryFilter`.
  `GET /api/v1/chaos/kill-switch` uses `ChaosPolicies.View`. An injection while engaged returns
  `409` with problem details.
- **Data plane:** directives return `204` while the switch is engaged.
- **Dashboard:** a persistent banner while engaged, and engage/release controls behind
  `ChaosPolicies.Admin`.
- **Tests:** the plan's kill-switch matrix column (viewer/operator `403`, admin `200`/`202`, data
  plane and anonymous `401`), `409` suppression, clearing on engage, release, interaction with
  expiry, and PostgreSQL coverage for the new migration.

Then Step 6: an authenticated Compose smoke test, the final handoff and evidence table, and a log
review for secret leakage.

## Follow-ups (not blocking Step 5)

- Extend CI's `container-build` job to start the API image and request `/_framework/blazor.web.js`,
  so a missing asset in the image fails CI.
- Switch the CSP from report-only to enforced once the dashboard runs with no violations.
- Now that the repository is public, add `dependency-review` to `main`'s required checks.
- Five `.idea/` files are tracked although `.gitignore` excludes `.idea/`; untrack them with
  `git rm --cached -r .idea` in a separate commit if wanted.
- `CpuSpike` is still accepted by the API but not executed (planned removal in Phase 5).
