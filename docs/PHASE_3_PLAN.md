# Stria Phase 3 Plan — Hosted Demo on Azure Container Apps

**Status (2026-11-19):** Steps 1 and 2 are built and green in CI, in PRs #23 and #24 (not merged
yet). Step 3 is in progress: the `Program.cs` refactor and the Bicep foundation pass their local
checks (158 tests; `bicep build`/`lint` clean). Prepared on 2026-11-18 after Phase 2 closed
([PHASE_2_HANDOFF.md](PHASE_2_HANDOFF.md)).

**Authorization gate:** Steps 1–3 below change only code, CI and documentation and need no Azure
account. **Step 4 onward creates billable Azure resources and must not start until Alvin explicitly
authorizes it**, with a region, a monthly budget and a teardown date agreed in writing (an issue or
PR comment is enough).

## 1. Objective

Deploy Stria as a reproducible, authenticated portfolio demo (Milestone 7 of
[STRIA_SERVICE_PLAN.md](STRIA_SERVICE_PLAN.md)). Phase 3 is complete when:

- A reviewer can open an HTTPS URL, sign in with Entra, and run a bounded experiment against the
  hosted `PaymentGateway`, with the kill switch and lease expiry working as they do locally.
- Only the Stria dashboard/control plane is public. The sample service and PostgreSQL are private.
- Images are immutable (tagged with the Git SHA), built and scanned in CI, and deployed by CI with
  OIDC (no long-lived Azure secrets in GitHub).
- Migrations run as a release job before the new revision, never on application startup.
- No secret is in the repository, Bicep parameters, workflow files or logs.
- The deployment, rollback and teardown are documented and have been executed once.

## 2. Baseline (from Phase 2)

- Entra sign-in (cookie + JWT bearer), three app roles, admin kill switch, per-service data-plane
  keys, antiforgery, rate limits, enforced CSP. 119 tests; CI requires `build-and-test`,
  `dependency-audit`, `dependency-review`, `container-build`.
- Compose-only assumptions that do not survive Azure (each is a step below):

| Local assumption | Why it breaks in Azure |
|---|---|
| `stria-dpkeys` Docker volume for Data Protection keys | Replicas don't share disk; cookies and antiforgery tokens fail after a restart or on a second replica |
| `Database:MigrateOnStartup=true` | Concurrent replicas race; a bad migration takes the app down with it |
| Password in the PostgreSQL connection string | Should be passwordless (managed identity) |
| Data-plane key and client secret in `.env` | Belong in Key Vault, referenced by the container apps |
| Kestrel sees the client directly | Behind ingress the scheme and client IP arrive in `X-Forwarded-*` headers; without them the OIDC redirect URI is built as `http://` and sign-in fails |
| Plain HTTP between containers | Ingress terminates TLS publicly; internal traffic stays in the environment |
| Rate limits in memory | Correct only with one replica; keep `maxReplicas: 1` for the demo |

## 3. Target mapping

| Compose service | Azure |
|---|---|
| `stria-server` | Container app, **external** ingress (HTTPS), `minReplicas 0–1`, `maxReplicas 1` |
| `sample-payment-gateway` | Container app, **internal** ingress only, `maxReplicas 1` |
| `db` (postgres + volume) | Azure Database for PostgreSQL Flexible Server, Burstable B1ms, Entra auth, backups on |
| Migrations (startup) | Container Apps **job**, manual trigger, runs an EF bundle before each release |
| `.env` secrets | Key Vault (RBAC mode); container app secrets with `keyVaultUrl` |
| Data Protection volume | Blob container + Key Vault key |
| `build:` | Images built in CI, pushed to Azure Container Registry (Basic), tagged `sha-<commit>` |
| Logs to stdout | Log Analytics workspace + workspace-based Application Insights |

Shared: one **user-assigned managed identity** (AcrPull, Key Vault Secrets User, Storage Blob Data
Contributor on the keys container, Key Vault Crypto Service Encryption User on the DP key,
PostgreSQL Entra principal). Resource group `rg-stria-demo`.

## 4. Work breakdown

### Step 1 — Cloud-ready code, no Azure needed

Each item is a small PR with tests; all behaviour is opt-in by configuration so Compose and the
test suite keep working unchanged.

- **Forwarded headers:** `UseForwardedHeaders` first in the pipeline (`XForwardedFor |
  XForwardedProto`), trusting only the ingress. Test that a forwarded `https` request produces an
  `https://…/signin-oidc` redirect URI. Re-check the HTTPS-redirection exemption for directive
  polling.
- **Data Protection:** when `DataProtection:BlobUri` is set, persist keys to Blob and protect them
  with a Key Vault key (`Azure.Extensions.AspNetCore.DataProtection.Blobs` / `.Keys`); otherwise
  keep the file system path used by Compose.
- **Passwordless PostgreSQL:** when `Database:UseEntraAuth=true`, build the Npgsql data source
  with a managed-identity token provider (`DefaultAzureCredential`); tests stay on password auth.
- **Startup validation:** extend the Production checks to require the Azure settings above when
  any one of them is set (fail fast on half-configured deployments).
- **Telemetry:** Azure Monitor OpenTelemetry distro when a connection string is present; an
  `ActivitySource("Stria")` span around injection, kill-switch changes and directive reads.

**Acceptance:** all existing tests pass, new configuration paths have tests, Compose and
`scripts/compose-smoke.sh` behave as before.

**As built (2026-11-18):** 139 tests pass (20 new). Compose sets none of the settings below and passes
the smoke script unchanged. Startup refuses a half-configured Azure deployment, naming the missing
keys but never their values.

| Setting (environment variable form) | Azure value | Effect | Code |
|---|---|---|---|
| `ForwardedHeaders__Enabled` | `true` | Trusts `X-Forwarded-For`/`-Proto` from the ingress | `Program.cs` |
| `DataProtection__BlobUri` | Blob URI of `keys.xml` in the `dpkeys` container | Key ring in Blob instead of the volume | `Security/DataProtectionSetup.cs` |
| `DataProtection__KeyVaultKeyUri` | Key Vault key URI | Wraps every key in the ring | same |
| `Database__UseEntraAuth` | `true` | Managed-identity token as the PostgreSQL password; the connection string must not contain one | `Data/StriaDataSource.cs` |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | From App Insights | Exports traces (`Stria` spans and Npgsql) | `Services/StriaTelemetry.cs` |
| `AZURE_CLIENT_ID` | User-assigned identity's client ID | Picks that identity for `DefaultAzureCredential` | (Azure SDK) |

The first four are all-or-nothing (`Security/AzureRuntimeSettings.cs`); forwarded headers alone are
allowed for other proxies. App Insights is optional.

### Step 2 — Release artifacts in CI, no Azure needed

- Build an EF migrations bundle (`dotnet ef migrations bundle --self-contained -r linux-x64`) and
  a migration-job image.
- Tag images `sha-<commit>`; never deploy `latest`. Build for `linux/amd64` explicitly (local
  Macs build arm64).
- Add an image vulnerability scan (e.g. Trivy) to `container-build`, failing on high/critical.
- Keep publishing out of PRs; pushing images is part of the deploy job in Step 5.

**Acceptance:** CI produces a scanned, SHA-tagged image set and a migration bundle on every push
to `main`.

**As built (2026-11-19):** `container-build` builds four `linux/amd64` images tagged
`sha-<commit>` (`stria-api`, `stria-migrator`, `stria-client`, `stria-sample`), applies the
migrations bundle to an empty PostgreSQL twice, and scans every image with Trivy (pinned by
digest), failing on fixable HIGH/CRITICAL findings. SARIF goes to code scanning from `main`.
The bundle builds its context with `Data/StriaDbContextDesignTimeFactory.cs` and is configured
like the app: `ConnectionStrings__Stria`, plus `Database__UseEntraAuth=true` in Azure.
Nothing is pushed; that is Step 5. The first scan found no fixable HIGH/CRITICAL issues in any
image, so `.trivyignore` is empty. Trivy sees only the OS layer of `stria-migrator`: the
single-file bundle carries no `deps.json`, so its NuGet packages are covered by
`dependency-audit` instead.

### Step 3 — Infrastructure as code, no Azure needed until deploy

- **Endpoint and service modules (refactor, no behaviour change):** routes move from `Program.cs`
  into `Endpoints/` (health, authentication, control plane, data plane) with named handlers and
  `TypedResults`; startup checks move into `Security/StartupValidation.cs`; persistence and
  authentication setup move into `AddStria…` extensions.
- `infra/main.bicep` with modules for the resource set in §3, `infra/main.bicepparam`
  (no secrets; typed, so `bicep build-params` rejects a misspelled or mistyped parameter), and
  `azure.yaml` for `azd`.
- `bicep build` in CI (lint only; no Azure login needed).
- Budget alert resource on the resource group.
- README "Deploy to Azure" section: prerequisites, one-time setup, deploy, verify, rollback,
  teardown.

**Acceptance:** `bicep build` is clean in CI; a reviewer can read exactly what will be created
and what it costs before anything is deployed.

**Progress (2026-11-19):**

| Ticket | State | Evidence |
|---|---|---|
| 3.0a Endpoint modules | Committed (`d0a3fbb`, branch `phase-3/endpoint-modules`) | 155 tests; route metadata identical to the old `Program.cs` |
| 3.0b Service-registration modules | Written, not committed | 158 tests; moved auth code and all five startup messages identical to the originals |
| 3.1 Bicep foundation | Written, not committed (`infra/`) | `az bicep build`, `build-params` and `lint` clean, offline |
| 3.2 Data and compute modules | Next | — |
| 3.3 `azure.yaml`, budget, CI lint job | To do | — |
| 3.4 README "Deploy to Azure" | To do | — |

3.1 covers the identity, Log Analytics (1 GB/day cap) with App Insights, ACR Basic, Key Vault
(RBAC, 7-day soft delete, no purge protection) with the Data Protection key, and storage with
shared keys off and a `dpkeys` container. The app identity's roles are scoped as narrowly as each
use allows: AcrPull on the registry, Secrets User on the vault, Crypto Service Encryption User on
the one key, Blob Data Contributor on the one container.

### Step 4 — Provision (requires authorization)

- Create the resource group, budget alert, and run `azd provision` (or the Bicep deployment).
- One-time: set the PostgreSQL Entra admin; create the managed identity's database principal with
  only the grants the app needs; store the client secret and data-plane key in Key Vault.
- Entra app registration: add the hosted `https://<app>/signin-oidc` and
  `https://<app>/signout-callback-oidc` redirect URIs.

### Step 5 — CI deploy with OIDC (requires authorization)

- GitHub environment `demo` with required reviewer (Alvin); federated credential for the
  workflow; no client secrets in GitHub.
- On `main` after all checks pass: push images to ACR, run the migration job, then update both
  container apps to the new `sha-` tag. Stop on any failure.

### Step 6 — Verify, document, hand off

- Hosted smoke test: adapt `scripts/compose-smoke.sh` to take the public URL (sample service and
  database must be unreachable from outside).
- Signed-in walkthrough with each role, as in Phase 2, recorded in a Phase 3 handoff.
- Rollback rehearsal: redeploy the previous `sha-` tag and confirm it serves.
- Teardown rehearsal documented (`azd down` or resource-group delete) and the date agreed.

## 5. Decisions needed from Alvin before Step 4

| Decision | Default proposal |
|---|---|
| Azure subscription and region | A region close to UW–Madison, e.g. `centralus` |
| Monthly budget cap and alert threshold | Low cap with an alert at 50% and 90% |
| Demo access model | Reviewers get `ChaosViewer` by default; `ChaosOperator` granted on request; `ChaosAdmin` only for Alvin |
| PostgreSQL always on vs. scheduled stop | Stop the server when not demoing |
| Custom domain | None (use the generated `azurecontainerapps.io` host) |
| Teardown date | Agree one when provisioning |

## 6. Risks

| Risk | Mitigation |
|---|---|
| Continuous cost from PostgreSQL even when idle | Budget alert; stop the server or tear down between demos |
| Sign-in breaks behind ingress (`http` redirect URI) | Forwarded-headers step with a test before any deploy |
| Second replica breaks cookies, antiforgery and rate limits | `maxReplicas: 1` until Data Protection and limits are shared |
| Public demo used for unrestricted fault injection | Roles gate injection; targets are only the owned sample service; kill switch; leases ≤ 1 h |
| Image architecture mismatch (arm64 Mac vs amd64 Azure) | Build in CI for `linux/amd64` only |
| Migration failure mid-release | Migration job runs before the revision switch; rollback is the previous tag + forward-only fix |

## 7. Definition of done

- [x] Cloud-specific behaviour is opt-in, tested, and Compose still passes the smoke script.
- [x] CI produces scanned, SHA-tagged images and a migration bundle.
- [ ] Bicep and `azure.yaml` describe the full resource set with no secrets.
- [ ] Deployment by CI uses OIDC and a protected environment.
- [ ] The hosted demo passes the hosted smoke test and a signed-in walkthrough for each role.
- [ ] Rollback and teardown are documented and rehearsed.
