# Stria Phase 3 Plan — Hosted Demo on Azure Container Apps

**Status (2026-11-23):** Steps 1–4 are complete; Step 5 is in progress (5.0 and 5.1 merged). Steps 1–3 (#23–#30) delivered cloud-ready code,
release artifacts, the `Program.cs` refactor, every Bicep module, `azure.yaml`, the budget, the
required `infra-validate` CI job and the README "Deploy to Azure" runbook; the API image runs
unprivileged (ticket 4.0). **Step 4 provisioned the hosted demo** on 2026-11-21/22 after the §5
decisions were made in writing, and Alvin's signed-in walkthrough on it succeeded; see "Step 4
progress". Steps 5 (CI deploy) and 6 (hosted smoke test, per-role walkthrough, rehearsals) remain.
Prepared on 2026-11-18 after Phase 2 closed ([PHASE_2_HANDOFF.md](PHASE_2_HANDOFF.md)).

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
| `db` (postgres + volume) | Azure Database for PostgreSQL Flexible Server, Burstable B1ms, Entra-only auth (password auth disabled), backups on. Public endpoint limited to Azure services by firewall; see the networking note below |
| Migrations (startup) | Container Apps **job**, manual trigger, runs an EF bundle before each release |
| `.env` secrets | Key Vault (RBAC mode); container app secrets with `keyVaultUrl` |
| Data Protection volume | Blob container + Key Vault key |
| `build:` | Images built in CI, pushed to Azure Container Registry (Basic), tagged `sha-<commit>` |
| Logs to stdout | Log Analytics workspace + workspace-based Application Insights |

Shared: one **user-assigned managed identity** (AcrPull, Key Vault Secrets User, Storage Blob Data
Contributor on the keys container, Key Vault Crypto Service Encryption User on the DP key,
PostgreSQL Entra principal). Resource group `rg-stria-demo`.

**PostgreSQL networking (accepted deviation, 2026-11-20).** §1 says PostgreSQL is private. The
demo uses the server's public endpoint instead, with the firewall rule that admits only Azure
services and password authentication disabled, so only Entra principals granted a role can sign in.
VNet integration would need a delegated subnet, a private DNS zone and a Container Apps environment
on a custom VNet, and the Sprint 4 one-time setup could no longer run from a laptop. The weakness
is that "Azure services" covers every Azure customer's IP ranges, so authentication is the real
barrier. That is acceptable for a demo with no real data. A deployment holding customer data
should use private access. Sprint 6.1 checks that the database refuses connections from outside
Azure.

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

**As built (2026-11-19):** `container-build` builds `linux/amd64` images tagged
`sha-<commit>` (`stria-api`, `stria-migrator`, `stria-sample`), applies the
migrations bundle to an empty PostgreSQL twice, and scans every image with Trivy (pinned by
digest), failing on fixable HIGH/CRITICAL findings. SARIF goes to code scanning from `main`.
A fourth image, `stria-client` (the dashboard on nginx), was removed on 2026-11-20: nothing
deployed it, because `stria-api` serves the WebAssembly client itself.
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

**Progress (2026-11-20):** Step 3 is complete.

| Ticket | State | Evidence |
|---|---|---|
| 3.0a/b Endpoint and service modules | Merged #25 | 158 tests; route metadata and startup messages identical to the old `Program.cs` |
| 3.1 Bicep foundation | Merged #26 | `az bicep build`, `build-params` and `lint` clean, offline |
| 3.2 PostgreSQL, environment, apps, migrate job | Merged #27 | Same gates; networking deviation recorded in §3 |
| 3.3 `azure.yaml`, budget, CI `infra-validate` job | Merged #28 | `infra-validate` (build, params ×2, lint with warnings as errors, format) is a required check |
| 3.4 README "Deploy to Azure" | Merged #30 | Every name checked against `infra/`, every `az` command against CLI 2.93; the runbook itself runs first in Step 4 |

3.1 covers the identity, Log Analytics (1 GB/day cap) with App Insights, ACR Basic, Key Vault
(RBAC, 7-day soft delete, no purge protection) with the Data Protection key, and storage with
shared keys off and a `dpkeys` container. The app identity's roles are scoped as narrowly as each
use allows: AcrPull on the registry, Secrets User on the vault, Crypto Service Encryption User on
the one key, Blob Data Contributor on the one container.

3.2 adds PostgreSQL Flexible Server (B1ms, Entra-only, the Azure-services firewall rule, database
`stria`), the Container Apps environment logging to the workspace, `stria-server` (external HTTPS,
0–1 replicas, startup/liveness probes on `/healthz` and readiness on `/readyz`), the internal-only
`sample-payment-gateway`, and the manual `stria-migrate` job (EF bundle, no retries, 10-minute
timeout). Secrets are Key Vault references through the shared identity, and the connection string
has no password (`Ssl Mode=VerifyFull`). `deployApps` (`STRIA_DEPLOY_APPS`, default false) leaves
the apps and the job out of the first provision, because they need images in ACR and secrets in
Key Vault first.

3.3 adds `azure.yaml` (infra only; images stay CI-built and CI-deployed), a `.azure/` gitignore
entry, and a monthly budget alerting resource-group Owners at 50% and 90% actual and 100% forecast
spend. Its start date (`STRIA_BUDGET_START_DATE`) is set once, because Azure rejects any change to
it; empty skips the budget. The `infra-validate` job pins Bicep, compiles the template and the
parameters, fails on any lint finding and checks formatting. It is a required check.

**Before Step 4** (open items, none blocking 3.4):

| Item | Why it matters | When |
|---|---|---|
| ~~`stria-api` final image runs as root~~ | Fixed in ticket 4.0: `USER $APP_UID`, key directory owned by `app`; CI asserts non-root and a written key ring | Done |
| One identity for the app and the migration job | The app gets DDL rights it doesn't need | Deferred from Step 4 (the demo runs with one identity): Step 5, second identity or a DML-only role |
| Bicep declares the image tag | Re-provisioning with an old `STRIA_IMAGE_TAG` would roll the apps back | Step 5: CI always passes the current tag |
| ~~Not verified offline~~ | azd read `main.bicepparam` (resource names came from `AZURE_ENV_NAME`), and the budget accepted `startDate` `2026-11-01` | Done in Step 4 |
| Sample service scales to zero | Nothing polls while it's at zero; the demo needs a way to wake it | Step 6 |
| ~~The §5 decisions~~ | Made in writing on 2026-11-21, except the teardown date (see §5) | Done |

### Step 4 — Provision (requires authorization)

- Create the resource group, budget alert, and run `azd provision` (or the Bicep deployment).
- One-time: set the PostgreSQL Entra admin; create the managed identity's database principal with
  only the grants the app needs; store the client secret and data-plane key in Key Vault.
- Entra app registration: add the hosted `https://<app>/signin-oidc` and
  `https://<app>/signout-callback-oidc` redirect URIs.

**Step 4 progress** (2026-11-21/22, Azure for Students subscription, `rg-stria-demo` in `centralus`):

| Runbook step | Outcome | Evidence |
|---|---|---|
| Pre-checks | `centralus` is in the subscription's allowed-regions policy; 7 resource providers had to be registered; Alvin is Owner | `az policy assignment list`, `az provider show`, `az role assignment list` |
| 1–2 First provision (no apps) | Succeeded in 7 min 23 s | Budget $10/month from 2026-11-01; workspace cap 0.05 GB/day (#38, set before the first provision); all 5 role assignments on their narrow scopes |
| 3 Database role | Entra admin = Alvin (a personal Microsoft account, so a `#EXT#` UPN); role `id-stria-demo` created; temporary firewall rule removed | `pgaadauth_create_principal`, `GRANT` |
| 4 Secrets | 3 secrets in Key Vault; the client secret was checked with a client-credentials token request; the data-plane hash matches its key | `az keyvault secret list` |
| 5 Images | ACR Tasks are paused on free-credit subscriptions, so the images were built locally with `docker buildx --platform linux/amd64` and pushed | `sha-ae9be7e…`, linux/amd64, UID 1654 |
| 6 Apps + migration | First attempt failed: Azure created the environment in **Express** mode, which rejects jobs and Key Vault secret references. Fixed in #39 (`environmentMode: 'WorkloadProfiles'`, API 2026-07-01); the empty environment was deleted and re-provisioned. Migration `stria-migrate` succeeded (3 migrations) | Live `environmentMode: WorkloadProfiles` |
| 7 Redirect URIs | Hosted `/signin-oidc` and `/signout-callback-oidc` added | Entra serves its sign-in page, no `AADSTS50011` |
| Verify | `/healthz` 200, `/readyz` 200, the faults API 401 when anonymous, `/` redirects to Entra with an `https` redirect URI (forwarded headers work), the database and sample service are unreachable from outside, Alvin's signed-in walkthrough succeeded | `curl`, browser |

The runbook was corrected afterwards in #40 (firewall-rule flags, the local image build, secret
entry, zsh comments, azd's role-assignment warning, job logs).

### Step 5 — CI deploy with OIDC (requires authorization)

- GitHub environment `demo` with required reviewer (Alvin); federated credential for the
  workflow; no client secrets in GitHub.
- On `main` after all checks pass: push images to ACR, run the migration job, then update both
  container apps to the new `sha-` tag. Stop on any failure.

**Decisions (Alvin, 2026-11-22):** deploy automatically on every push to `main`, paused for
Alvin's approval in the `demo` environment; if PostgreSQL is stopped, fail early with a clear
message instead of starting it; do the identity split (5.4) in this sprint; when the hosted smoke
test fails, roll back to the previous tag automatically.

**Step 5 progress:**

| Ticket | State | Evidence |
|---|---|---|
| 5.0 Deploy identity + OIDC trust | Merged #42, provisioned 2026-11-23 | `id-stria-deploy-demo` with federated credential `github-demo`; 6 roles, each on one resource (AcrPush on the registry, Container Apps Contributor on both apps, Jobs Contributor on `stria-migrate`, Managed Identity Operator on `id-stria-demo`, Reader on PostgreSQL). GitHub environment `demo`: reviewer Alvin, protected branches only, 6 variables |
| 5.1 `scripts/hosted-smoke.sh` | Merged #43 | 11 checks pass live; planted defects fail (exit 2 misuse, exit 1 failure) |
| 5.2 Deploy job | Done: merged #46, fix #47; deployed 2026-11-23 | Run 45253367067 deployed `sha-b790e3b` end to end after approval: OIDC login, push, migration job, both apps on new ready revisions, 11 smoke checks green (3 min after approval). The first run (#46) exercised the rollback for real |
| 5.3 Live image tag for `azd provision` | In progress | `scripts/preprovision.sh`, declared in `azure.yaml`, reads all three images, requires one `sha-` tag and sets `STRIA_IMAGE_TAG`; fails on PostgreSQL not Ready or any `az` error other than not-found. 13 stub scenarios on bash 3.2 and 5.3, a planted defect caught; live `azd hooks run preprovision` reset a planted `sha-stale` to `sha-b790e3b` |
| 5.4 Separate migrator and app identities | Planned | `id-stria-migrate` owns the tables; `id-stria-demo` keeps DML only (`GRANT SELECT, INSERT, UPDATE, DELETE` plus `ALTER DEFAULT PRIVILEGES`) |

**OIDC subject.** This repository uses GitHub's immutable subjects (`use_immutable_subject:
true`), so the federated credential trusts
`repo:alvinl33-1@174841578/Stria@1457068099:environment:demo`, with owner and repository IDs
instead of names. The `:environment:demo` suffix follows GitHub's documented format and is
confirmed by the first login in 5.2. If it is wrong, Entra's `AADSTS700213` error prints the
subject GitHub sent, and `githubDeploySubject` in `main.bicep` is the one line to change.

**5.2 design (to be built and verified in the ticket):**

1. `container-build` saves the three scanned images and uploads them as an artifact, kept for one
   day, only on pushes to `main`. The deploy pushes those exact bytes and never rebuilds.
2. A `deploy` job in `ci.yml` with `needs: [build-and-test, container-build, dependency-audit,
   infra-validate]`, `if: github.event_name == 'push' && github.ref == 'refs/heads/main'`,
   `environment: demo`, `permissions: id-token: write, contents: read`, a `concurrency` group so
   deploys never overlap, and a timeout. `dependency-review` must not be in `needs`: it runs only
   on pull requests, so on a push it is skipped, and a skipped dependency skips the deploy.
3. Steps: `azure/login` with OIDC (the `demo` variables); stop unless PostgreSQL is `Ready`
   (needs a `POSTGRES_SERVER_NAME` variable); load the images, `az acr login`, push the
   `sha-<commit>` tags; record the tag `stria-server` runs now; point the migration job at the new
   image, start it, wait for `Succeeded`; update both apps; run `scripts/hosted-smoke.sh`; on
   exit 1, update both apps back to the recorded tag and fail the job.
4. Keep the Azure commands in `scripts/deploy-demo.sh`, which the job calls, so the same steps
   can run by hand. Manual redeploys and rollbacks use a separate `deploy.yml` workflow
   (`workflow_dispatch` only, an `image_tag` input, same `demo` environment and concurrency
   group) that runs the script without `--push`, because that tag is already in the registry.
   *Decided 2026-11-23:* a tag input on `ci.yml` would rebuild and scan the current commit before
   deploying an older one, and its `needs` chain would skip the deploy when the build jobs skip.
5. Rollback replaces images but not the database: migrations stay forward-only, so a release
   must not drop or rename anything the previous image still uses.

**First run (2026-11-23, run 45233761299, `a614ec9`):** the OIDC login worked with the
`:environment:demo` subject, `az acr login` worked with AcrPush only, the migration job ran, and
`az containerapp update` succeeded with Managed Identity Operator (the new revision came up
Healthy). The artifact took 3 s to download and 4 s to load. The deploy still failed: the
revision wait read `az -o tsv` for a JMESPath list as one tab-separated row, but tsv prints a list
of scalars one value per line, so the image came back empty. The rollback worked (both apps and
the job back on `sha-ae9be7e6`, smoke test green afterwards), but its wait had the same bug, so it
reported exit 3. The stubbed `az` used in testing printed one row, the same wrong assumption.
Fixed in 5.2a by wrapping the query in a second list, which tsv prints as one row.

**Second run (2026-11-23, run 45253367067, `b790e3b`):** deployed end to end. Migration 44 s,
`stria-server` ready revision 39 s after its update, `sample-payment-gateway` 38 s, smoke test
2 s; 3 minutes from approval to `Deployed`.

**5.3 findings (2026-11-25, azd 1.39.1):**
- `shell: sh` hooks run as `bash <file>` from PATH (not `/bin/sh`), so the hook stays bash and
  must work on bash 3.2.
- azd exports the environment's values to the hook; a nested `azd env set` writes to the
  environment being provisioned (`AZURE_ENV_NAME` is passed down).
- **`azd provision --preview` doesn't run preprovision hooks.** With `sha-stale` planted, the
  preview printed no hook output and the value was still `sha-stale` afterwards. The README says to
  run `azd hooks run preprovision` before a preview.
- azd's preview summary can't show an image change: it prints `* properties.template.containers`
  without values, and that line appears even when the tag is right (probe-order noise). Use
  `az deployment group what-if` for the detail.

### Step 6 — Verify, document, hand off

- Hosted smoke test: adapt `scripts/compose-smoke.sh` to take the public URL (sample service and
  database must be unreachable from outside).
- Signed-in walkthrough with each role, as in Phase 2, recorded in a Phase 3 handoff.
- Rollback rehearsal: redeploy the previous `sha-` tag and confirm it serves.
- Teardown rehearsal documented (`azd down` or resource-group delete) and the date agreed.

## 5. Decisions made before Step 4

Agreed in writing by Alvin on 2026-11-21.

| Decision | Default proposal | Decided |
|---|---|---|
| Azure subscription and region | A region close to UW–Madison, e.g. `centralus` | **Azure for Students** on Alvin's personal account ($100 credit for 12 months; a hard spending limit, so the subscription is disabled rather than billed when the credit runs out). **`centralus`**, which the subscription's region policy allows (alternatives: `northcentralus`, `eastus2`, `westus3`, `mexicocentral`) |
| Monthly budget cap and alert threshold | Low cap with an alert at 50% and 90% | **$10/month**, alerts at 50% and 90% actual and 100% forecast. With PostgreSQL stopped between demos the estimate is about $9–10, so the 90% alert effectively means "PostgreSQL was left on" |
| Demo access model | Reviewers get `ChaosViewer` by default; `ChaosOperator` granted on request; `ChaosAdmin` only for Alvin | **As proposed.** Reviewers are guest accounts in the Entra tenant; `ChaosOperator` is granted on request for a limited time, then removed |
| PostgreSQL always on vs. scheduled stop | Stop the server when not demoing | **Stopped between demos.** Azure restarts a stopped server after 7 days, so it has to be stopped again |
| Custom domain | None (use the generated `azurecontainerapps.io` host) | **None** |
| Teardown date | Agree one when provisioning | **Not set yet:** the credit's expiry date is still unknown. Tear down early if the remaining credit drops below $15 |

## 6. Risks

| Risk | Mitigation |
|---|---|
| Continuous cost from PostgreSQL even when idle | Budget alert; stop the server or tear down between demos |
| Sign-in breaks behind ingress (`http` redirect URI) | Forwarded-headers step with a test before any deploy |
| Second replica breaks cookies, antiforgery and rate limits | `maxReplicas: 1` until Data Protection and limits are shared |
| Public demo used for unrestricted fault injection | Roles gate injection; targets are only the owned sample service; kill switch; leases ≤ 1 h |
| Image architecture mismatch (arm64 Mac vs amd64 Azure) | Build in CI for `linux/amd64` only |
| Migration failure mid-release | Migration job runs before the revision switch; rollback is the previous tag + forward-only fix |
| A platform default changes under the template (seen in Step 4: new environments defaulted to Express) | State important modes explicitly in Bicep (`environmentMode`); a preview before every provision; read the live resource after creating it |
| The student credit runs out and the subscription is disabled | $10 budget with alerts, PostgreSQL stopped between demos, a 0.05 GB/day log cap, and teardown once the credit drops below $15 |

## 7. Definition of done

- [x] Cloud-specific behaviour is opt-in, tested, and Compose still passes the smoke script.
- [x] CI produces scanned, SHA-tagged images and a migration bundle.
- [x] Bicep and `azure.yaml` describe the full resource set with no secrets.
- [ ] Deployment by CI uses OIDC and a protected environment.
- [ ] The hosted demo passes the hosted smoke test and a signed-in walkthrough for each role.
- [ ] Rollback and teardown are documented and rehearsed.
