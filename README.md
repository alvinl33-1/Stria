# Stria

Stria is a .NET 10 chaos engineering demo. It provides a web dashboard and HTTP API for creating time-limited fault experiments against registered services, stores experiment history in PostgreSQL, and serves active fault directives to participating services.

## Current capabilities

Stria recognizes three target names: `PaymentGateway`, `AuthService`, and `InventoryAPI`. Fault types accepted by the API are:

- `Latency`: adds a delay of 1–60,000 ms to a selected percentage of requests.
- `HttpFailure`: returns a selected HTTP status in the 500–599 range to a selected percentage of requests.
- `CpuSpike`: records an experiment, but the sample service does not execute this fault.
- `None`: clears the active fault for the target.

Experiments have a lease from 1 to 3,600 seconds, defaulting to 300 seconds. Requests that omit the percentage use 100%; `HttpFailure` defaults to status 503. The server records the initiating identity from the caller's validated Entra claims (object ID, plus display name); a caller-supplied `TriggeredBy` value is ignored.

The dashboard is served by the Stria ASP.NET Core host with interactive Blazor components. PostgreSQL stores experiment state. The `sample-payment-gateway` container polls Stria for `PaymentGateway` directives, authenticating with its own data-plane key, and applies latency or HTTP failure faults to its `/work` endpoint. Its `/healthz` endpoint remains available while faults are active. The database is only exposed on the internal Compose network; the sample service port is bound to loopback.

## Run with Docker Compose

Create a `.env` file beside `compose.yaml` (start from `.env.example`) containing a local database password, the Entra test-tenant settings described in [docs/ENTRA_SETUP.md](docs/ENTRA_SETUP.md), and the sample service's data-plane key:

```dotenv
STRIA_DB_PASSWORD=replace-with-a-strong-local-password
STRIA_TENANT_ID=<tenant-id>
STRIA_CLIENT_ID=<client-id>
STRIA_CLIENT_SECRET=<client-secret>
STRIA_PAYMENTGATEWAY_KEY=<key>
STRIA_PAYMENTGATEWAY_KEY_SHA256=<sha256-hex-of-key>
```

Generate the key and its hash once:

```sh
KEY=$(openssl rand -base64 32 | tr '+/' '-_' | tr -d '=')
echo "STRIA_PAYMENTGATEWAY_KEY=$KEY"
echo "STRIA_PAYMENTGATEWAY_KEY_SHA256=$(printf %s "$KEY" | shasum -a 256 | cut -d' ' -f1)"
```

The `.env` file is gitignored and is used by Compose to configure PostgreSQL, Stria's connection string, sign-in and data-plane keys. Stria receives only the key's hash; the sample service receives the key. Compose runs Stria in the Production environment, so it refuses to start when an identity setting is missing or no data-plane key hash is configured. Then, from the repository root, run:

```sh
docker compose up --build
```

Compose starts PostgreSQL, waits for its health check, starts Stria, and starts the sample service. In this configuration, Stria applies database migrations on startup. The sample service is available at:

- `http://localhost:8080` — Stria dashboard and HTTP API.
- `http://localhost:8081/work` — sample service work endpoint.
- `http://localhost:8081/healthz` — sample service health endpoint.

Stop the stack with `Ctrl+C`; use `docker compose down` to stop and remove the containers. The named `stria-pgdata` volume retains database contents when containers are removed, and `stria-dpkeys` retains Data Protection keys.

Stria runs as the unprivileged `app` user (UID 1654). If your `stria-dpkeys` volume was created by an older image that ran as root, the new one can't read or write it: pages still load, but keys don't persist, and the logs show `UnauthorizedAccessException`. Recreate that one volume (you'll sign in again):

```sh
docker compose down
docker volume rm stria_stria-dpkeys    # the name is <project>_stria-dpkeys; see `docker volume ls`
docker compose up --build -d
```

Don't use `docker compose down -v` for this: it deletes every volume, including the database.

Compose migrates on startup for convenience. Releases run the migrations bundle instead, as a separate job before the new revision starts; the same image works against the Compose database:

```sh
docker build --target migrator -f Stria.API/Dockerfile -t stria-migrator:local .
docker run --rm --network stria_default \
  -e ConnectionStrings__Stria="Host=db;Database=stria;Username=stria;Password=$STRIA_DB_PASSWORD" \
  stria-migrator:local
```

For an automated smoke test of the running stack, run `scripts/compose-smoke.sh` from the repository root. It reads the sample service's key from `.env` and checks health, that anonymous callers get `401` on every control and directive route, data-plane key isolation, the enforced Content Security Policy, and that the image serves the Blazor script. It first waits up to 60 seconds for `/readyz` (set `STRIA_READY_TIMEOUT` to change this), so it can run straight after `docker compose up -d`. Signed-in flows need a browser.

For a manual check, open the dashboard, inject bounded latency for `PaymentGateway`, and call `http://localhost:8081/work` repeatedly to observe the delay. Clear the fault and confirm the delay stops after the lease expires. The API process and database readiness endpoints should both return `200`:

```sh
curl -i http://localhost:8080/healthz
curl -i http://localhost:8080/readyz
```

## Deploy to Azure (hosted demo)

> **First provisioned on 2026-11-22** by following this runbook on an Azure for Students subscription; the notes below record what that run needed. Since Sprint 5, every push to `main` deploys through CI after an approval (see [Release a new version](#release-a-new-version)); provisioning stays manual.
>
> The blocks are written for bash. In zsh (the macOS default), run `setopt interactivecomments` first: without it, interactive zsh doesn't treat `#` as a comment, so the inline `# …` notes are passed to the commands as arguments.

The hosted demo is one resource group, `rg-stria-demo`, described entirely by `infra/`. It contains:
- PostgreSQL Flexible Server (B1ms, Entra sign-in only).
- A Container Apps environment with `stria-server` (public HTTPS), `sample-payment-gateway` (internal only) and the `stria-migrate` job.
- Azure Container Registry, Key Vault, a storage account for the Data Protection keys, Log Analytics with Application Insights, a monthly budget, and three user-assigned managed identities, each holding only the roles its workload uses:
  - `id-stria-<env>`: `stria-server` and the migration job.
  - `id-stria-sample-<env>`: the sample service. It can pull images and read the one secret `paymentgateway-key`, and has no database role.
  - `id-stria-deploy-<env>`: the GitHub deploy job (OIDC, no secret).

No password or key is stored in the repository or in the Bicep parameters.

### Prerequisites

- An Azure subscription where you are **Owner**. The deployment creates role assignments, which Contributor can't do.
- Azure CLI with Bicep (`az bicep version`), the Azure Developer CLI (`azd`), `psql` 16 or later, and `jq`.
- The Entra app registration from [docs/ENTRA_SETUP.md](docs/ENTRA_SETUP.md), with its tenant ID, client ID and client secret.
- A clean checkout of the commit you want to deploy. Image tags are `sha-<commit>`.

### One-time setup

**1. Create the azd environment and the resource group.** `STRIA_BUDGET_START_DATE` is the first day of the current month. It is set once and never changed, because Azure rejects any change to a budget's start date.

```sh
az login && azd auth login
azd env new demo
azd env set AZURE_LOCATION centralus
azd env set AZURE_RESOURCE_GROUP rg-stria-demo
az group create --name rg-stria-demo --location centralus --tags project=stria

azd env set STRIA_OPERATOR_PRINCIPAL_ID "$(az ad signed-in-user show --query id -o tsv)"
azd env set STRIA_TENANT_ID <entra-tenant-id>
azd env set STRIA_CLIENT_ID <entra-client-id>
azd env set STRIA_BUDGET_AMOUNT 25
azd env set STRIA_BUDGET_START_DATE "$(date +%Y-%m-01)"
azd env set STRIA_BUDGET_EMAIL you@example.com    # optional; resource group Owners are always notified
```

**2. First provision, without the apps.** The apps need images in the registry and secrets in Key Vault, and neither exists yet. `STRIA_DEPLOY_APPS` defaults to `false`, so this run creates everything except the two apps and the job.

```sh
azd provision --preview    # review what will be created
azd provision
eval "$(azd env get-values)"   # loads AZURE_KEY_VAULT_NAME, POSTGRES_HOST, ... into this shell
```

`azd` warns "Role assignment permissions at the target scopes were not verified" on every provision. Its pre-check only looks at the subscription, while the role assignments sit on individual resources. If you are Owner of the subscription (`az role assignment list --assignee <your object id> --scope /subscriptions/<id>`), the permission is inherited and you can answer **Yes**. The preview also lists `Modify` for existing resources whose service-filled defaults (for example PostgreSQL `storage.tier`) aren't in the template; those are what-if noise. Stop only on a `Delete`.

**3. Give the managed identity a database role.** The server accepts only Entra tokens, so first make yourself its Entra admin. Then add a temporary firewall rule for your IP; the permanent rule admits only Azure services.

```sh
ME="$(az ad signed-in-user show --query userPrincipalName -o tsv)"
az postgres flexible-server microsoft-entra-admin create -g "$AZURE_RESOURCE_GROUP" -s "$POSTGRES_SERVER_NAME" \
  --display-name "$ME" --object-id "$STRIA_OPERATOR_PRINCIPAL_ID" --type User
MYIP=$(curl -s https://api.ipify.org)
az postgres flexible-server firewall-rule create -g "$AZURE_RESOURCE_GROUP" -s "$POSTGRES_SERVER_NAME" \
  -n operator-setup --start-ip-address "$MYIP" --end-ip-address "$MYIP"

export PGPASSWORD="$(az account get-access-token --resource-type oss-rdbms --query accessToken -o tsv)"
psql "host=$POSTGRES_HOST dbname=postgres sslmode=verify-full sslrootcert=system user=$ME" \
  -c "SELECT * FROM pgaadauth_create_principal('id-stria-demo', false, false);"
psql "host=$POSTGRES_HOST dbname=stria sslmode=verify-full sslrootcert=system user=$ME" \
  -c 'GRANT USAGE, CREATE ON SCHEMA public TO "id-stria-demo";'

az postgres flexible-server firewall-rule delete -g "$AZURE_RESOURCE_GROUP" -s "$POSTGRES_SERVER_NAME" -n operator-setup --yes
unset PGPASSWORD
```

The `firewall-rule` commands take the server with `-s` and the rule name with `-n`. If you sign in with a personal Microsoft account, `$ME` is your `…#EXT#@<tenant>.onmicrosoft.com` name; keep it quoted, and the `psql` user must be exactly that name. The role name must match the identity's name, `id-stria-<environment>`. The app and the migration job currently share this identity, so it owns the tables it creates. Splitting DDL from DML rights is an open Step 4 decision.

**4. Store the secrets in Key Vault.** `read -s` keeps the values out of your shell history. Run the `read` line **on its own**, then paste the secret: if you paste the whole block, `read` can take an empty line and the first `secret set` fails with `--value` missing. Paste the client secret's **Value** (copy icon, shown only once at creation), not its Secret ID or the table row. On macOS, `ENTRA_SECRET="$(pbpaste)"` is a reliable alternative; check `echo ${#ENTRA_SECRET}` (about 40) before storing. Generate the data-plane key as in [Run with Docker Compose](#run-with-docker-compose).

```sh
read -rs ENTRA_SECRET && echo
KEY=$(openssl rand -base64 32 | tr '+/' '-_' | tr -d '=')
az keyvault secret set --vault-name "$AZURE_KEY_VAULT_NAME" --name entra-client-secret --value "$ENTRA_SECRET" -o none
az keyvault secret set --vault-name "$AZURE_KEY_VAULT_NAME" --name paymentgateway-key --value "$KEY" -o none
az keyvault secret set --vault-name "$AZURE_KEY_VAULT_NAME" --name paymentgateway-key-sha256 \
  --value "$(printf %s "$KEY" | shasum -a 256 | cut -d' ' -f1)" -o none
unset ENTRA_SECRET KEY
```

**5. Build the images in the registry.** ACR builds them for `linux/amd64` from your checkout. Until Step 5 these are not the CI-scanned artifacts, so build only from a commit whose CI run passed. **On a free-credit subscription (Azure for Students, free trial), ACR Tasks are paused** and `az acr build` fails with `TasksOperationsNotAllowed`; use the local build after this block instead.

```sh
TAG="sha-$(git rev-parse HEAD)"; REG="${AZURE_CONTAINER_REGISTRY_ENDPOINT%%.*}"
az acr build -r "$REG" -t "stria-api:$TAG" -f Stria.API/Dockerfile --target final --platform linux/amd64 .
az acr build -r "$REG" -t "stria-migrator:$TAG" -f Stria.API/Dockerfile --target migrator --platform linux/amd64 .
az acr build -r "$REG" -t "stria-sample:$TAG" -f Stria.SampleService/Dockerfile --platform linux/amd64 .
```

Local build and push (needs Docker with BuildKit; on Apple Silicon the amd64 build is emulated and slower). `--provenance=false --sbom=false` keeps each image a single manifest, like CI's `docker build`:

```sh
TAG="sha-$(git rev-parse HEAD)"; LOGIN="$AZURE_CONTAINER_REGISTRY_ENDPOINT"
az acr login -n "${LOGIN%%.*}"
docker buildx build --platform linux/amd64 --provenance=false --sbom=false -f Stria.API/Dockerfile --target final -t "$LOGIN/stria-api:$TAG" --push .
docker buildx build --platform linux/amd64 --provenance=false --sbom=false -f Stria.API/Dockerfile --target migrator -t "$LOGIN/stria-migrator:$TAG" --push .
docker buildx build --platform linux/amd64 --provenance=false --sbom=false -f Stria.SampleService/Dockerfile -t "$LOGIN/stria-sample:$TAG" --push .
```

**6. Second provision, with the apps, then migrate.**

```sh
azd env set STRIA_IMAGE_TAG "$TAG"
azd env set STRIA_DEPLOY_APPS true
azd provision
eval "$(azd env get-values)"
az containerapp job start -n "$STRIA_MIGRATE_JOB_NAME" -g "$AZURE_RESOURCE_GROUP"
az containerapp job execution list -n "$STRIA_MIGRATE_JOB_NAME" -g "$AZURE_RESOURCE_GROUP" \
  --query "sort_by([], &properties.startTime)[-1].properties.status" -o tsv    # repeat until Succeeded
```

`STRIA_IMAGE_TAG` names the commit the images were built from. If `main` moved since step 5 with infra-only changes, keep the old tag rather than recomputing it from `HEAD`, or the apps point at images that don't exist. To read a migration run's output, use the job's container name, `migrate` (the log stream only works while the replica runs). A first run logs a failed `SELECT … "__EFMigrationsHistory"` before creating that table, and a missing `libgssapi_krb5.so.2` (Kerberos, unused); both are expected:

```sh
az containerapp job logs show -n "$STRIA_MIGRATE_JOB_NAME" -g "$AZURE_RESOURCE_GROUP" --container migrate --tail 40
```

**7. Register the hosted redirect URIs** on the Entra app registration (Authentication → Web): `$STRIA_SERVER_URL/signin-oidc` and `$STRIA_SERVER_URL/signout-callback-oidc`.

### Verify

```sh
curl -s "$STRIA_SERVER_URL/healthz"    # the first call after idle takes several seconds: cold start from zero
curl -s -o /dev/null -w '%{http_code}\n' "$STRIA_SERVER_URL/readyz"                # 200
curl -s -o /dev/null -w '%{http_code}\n' "$STRIA_SERVER_URL/api/v1/chaos/faults"   # 401 when anonymous
nc -z -G 5 "$POSTGRES_HOST" 5432 || echo "database refuses this IP (expected)"
```

`scripts/hosted-smoke.sh "$STRIA_SERVER_URL"` runs the automated checks (health, anonymous rejection, the https sign-in redirect, the CSP, and that the sample service isn't public); the deploy job runs it after every release. Then open `$STRIA_SERVER_URL`, sign in, and confirm the role you hold matches what the dashboard allows. The sample service has no public address, so nothing outside the environment can reach it.

### Release a new version

Every push to `main` deploys, once the `deploy` job is approved:

1. `container-build` builds and scans the three images, then uploads exactly those bytes as a one-day artifact. Nothing is rebuilt for the deploy.
2. The `deploy` job waits in the `demo` environment. Approve it under the workflow run's **Review deployments**. **Start PostgreSQL first** if it is stopped: the job checks and stops before changing anything.
3. It signs in to Azure with OIDC (no stored secret) and runs `scripts/deploy-demo.sh --push sha-<commit>`. The script pushes the images, records the tag running now, runs the `stria-migrate` job and waits for `Succeeded`, then moves both apps to the new tag and waits for each new revision to be the ready one. Last, it runs `scripts/hosted-smoke.sh`.
4. If anything fails after the first change, the script moves what it changed back to the recorded tag and smoke-tests the result.

| Exit code | Meaning |
|---|---|
| 0 | Deployed and the smoke test passed |
| 1 | Failed; nothing changed, or the rollback passed the smoke test |
| 2 | Usage: bad tag or a missing setting, nothing called |
| 3 | The rollback did not pass the smoke test: the demo needs a person now |

The script also runs by hand, with the azd environment's values, for a tag that is already in the registry:

```sh
set -a; eval "$(azd env get-values)"; set +a
scripts/deploy-demo.sh sha-<commit>
```

**Provisioning never changes the images.** Bicep declares each image as `…:${STRIA_IMAGE_TAG}`, so a stale value would roll the apps back after a CI deploy. An azd `preprovision` hook (`scripts/preprovision.sh`, declared in `azure.yaml`) runs before every `azd provision` and `azd up`, reads the images that `stria-server`, `sample-payment-gateway` and `stria-migrate` run, and sets `STRIA_IMAGE_TAG` to their shared `sha-` tag. It only reads Azure, and it stops the provision when it can't be sure:

| The hook sees | It does |
|---|---|
| The images share one `sha-` tag | Sets `STRIA_IMAGE_TAG` to it (prints the old and new value) and continues |
| No app or job yet (first provisions) | Keeps `STRIA_IMAGE_TAG` as set in the runbook and continues |
| Images on different tags, or not `<registry>/<repo>:sha-<commit>` | Fails: deploy one tag with `deploy.yml` (below), then provision |
| PostgreSQL not `Ready` | Fails with the start command |
| Any other `az` error (signed out, wrong subscription, network) | Fails and prints the error |

To change the images, deploy. Don't provision while a deploy is running (`gh run list`): the hook would read the old tag and the provision would write it back.

**Run the hook yourself before a preview.** `azd provision --preview` doesn't run it, so the what-if would compare against a stale `STRIA_IMAGE_TAG` and show the images changing:

```sh
azd hooks run preprovision
azd provision --preview
```

### Roll back

A failed release rolls itself back. To return to an earlier release on purpose, run the **Deploy** workflow (`.github/workflows/deploy.yml`) from `main` with `image_tag` set to that `sha-` tag. It needs the same approval, never builds, and runs the same script without `--push`, so the tag must already be in the registry. `scripts/deploy-demo.sh <tag>` does the same from a terminal.

Migrations are forward-only. A rollback runs the previous code against the current schema, so every migration has to stay compatible with the release before it. If one isn't, roll forward with a fix instead.

### Save money between demos

```sh
az postgres flexible-server stop  -g "$AZURE_RESOURCE_GROUP" -n "$POSTGRES_SERVER_NAME"
az postgres flexible-server start -g "$AZURE_RESOURCE_GROUP" -n "$POSTGRES_SERVER_NAME"
```

A stopped server bills only for storage, and `/readyz` fails, so the app takes no traffic. Azure starts a stopped server again automatically after 7 days, so stop it again if the demo is still idle. Both apps scale to zero on their own.

### Tear down

```sh
az group delete --name rg-stria-demo --yes
az keyvault purge --name "$AZURE_KEY_VAULT_NAME"    # soft delete keeps the name reserved for 7 days
```

Purge the vault before re-creating the group. Otherwise the next provision gets the same vault name and collides with the soft-deleted vault. Also remove the two hosted redirect URIs from the app registration, and delete `.azure/demo/` locally.

### Estimated monthly cost

These are list prices in USD (East US, November 2026); `centralus` is similar. Confirm with the [Azure pricing calculator](https://azure.microsoft.com/pricing/calculator/) before provisioning.

| Resource | Basis | ≈ per month |
|---|---|---|
| PostgreSQL B1ms compute | $0.017/hour × 730 | $12.41, $0 while stopped |
| PostgreSQL storage, 32 GiB | $0.115/GiB | $3.68, also billed while stopped |
| Container Registry Basic | about $0.167/day | $5 |
| Container Apps (consumption) | monthly free grant per subscription: 180,000 vCPU-s, 360,000 GiB-s, 2 million requests. Both apps scale to zero | about $0 for demo traffic |
| Log Analytics + Application Insights | first 5 GB/month free; ingestion capped at 0.05 GB/day (about 1.5 GB/month) | $0. The cap keeps even the worst case inside the free grant |
| Key Vault, storage, identity, budget | per operation or per GB | under $0.50 |
| **Total** | | **about $21 always on; about $9–10 with PostgreSQL stopped between demos** |

The budget alerts at 50% and 90% of `STRIA_BUDGET_AMOUNT`, and when spend is forecast to pass 100%. It only notifies; it never stops anything. A $20 budget is below the always-on estimate, so either set it to about $25 or stop PostgreSQL between demos.

## CI and quality gates

GitHub Actions runs on pushes and pull requests to `main`:

| Job | What it proves |
|---|---|
| `build-and-test` | Formatting is clean, the Release build has no warnings, all tests pass, and TRX/coverage evidence is uploaded |
| `dependency-audit` | No known vulnerable direct or transitive NuGet packages are referenced |
| `dependency-review` | A pull request does not introduce high-severity vulnerable dependencies (public repositories; private repositories need GitHub Advanced Security) |
| `container-build` | Builds the control-plane, migration-job and sample-service images for `linux/amd64`, tagged `sha-<commit>` (never `latest`). The control-plane image starts in Production mode and serves the Blazor script its pages reference; the migrations bundle brings an empty PostgreSQL to the latest migration and is a no-op on a second run; Trivy fails the job on fixable HIGH/CRITICAL findings in any image (accepted findings go in `.trivyignore`) |
| `infra-validate` | `infra/` compiles with the pinned Bicep version (template, and the parameters file with and without the apps and budget), has no lint findings (warnings count), and is formatted (`az bicep format`). Needs no Azure login |
| `deploy` | Pushes to `main` only, after approval in the `demo` environment: deploys the scanned images to the hosted demo, smoke-tests them, and rolls back on failure (see [Release a new version](#release-a-new-version)). Not a required check: pull requests skip it |

Integration tests use Testcontainers and PostgreSQL. CI sets `STRIA_REQUIRE_DOCKER_TESTS=true`, so the job fails rather than silently skipping PostgreSQL coverage when Docker cannot start. Dependabot checks NuGet packages, GitHub Actions, and Docker base images weekly.

Run the same .NET gates locally with:

```sh
dotnet restore Stria.sln
dotnet format Stria.sln --verify-no-changes
dotnet build Stria.sln --configuration Release --no-restore -warnaserror
dotnet test Stria.sln --configuration Release --no-build
```

And the infrastructure gates (Azure CLI with Bicep, no login):

```sh
az bicep build --file infra/main.bicep --stdout > /dev/null
az bicep build-params --file infra/main.bicepparam --stdout > /dev/null
az bicep lint --file infra/main.bicep --diagnostics-format sarif > /tmp/bicep-lint.sarif
jq '[.runs[].results[]] | length' /tmp/bicep-lint.sarif   # 0 findings
```

`main` branch protection requires `build-and-test`, `dependency-audit`, `dependency-review`, `container-build`, and `infra-validate`, and branches must be up to date with `main` before merging. `dependency-review` needs a public repository (or GitHub Advanced Security); drop it from the required checks if the repository becomes private. Administrators can bypass these rules; do not use that bypass for normal changes.

The Phase 2 security plan is in [docs/PHASE_2_PLAN.md](docs/PHASE_2_PLAN.md); its evidence is in [docs/PHASE_2_HANDOFF.md](docs/PHASE_2_HANDOFF.md). Phase 3 (hosted demo on Azure Container Apps) is planned in [docs/PHASE_3_PLAN.md](docs/PHASE_3_PLAN.md), with one figure per sprint in [docs/PHASE_3_SPRINTS.md](docs/PHASE_3_SPRINTS.md); the infrastructure is described in `infra/` and was first provisioned in Sprint 4, and [Deploy to Azure](#deploy-to-azure-hosted-demo) is the runbook.

## Authentication

Operators sign in with Microsoft Entra ID (OpenID Connect, authorization-code flow); see [docs/ENTRA_SETUP.md](docs/ENTRA_SETUP.md) for the test-tenant setup. Access is controlled by Entra app roles:

| Role | Can |
|---|---|
| `ChaosViewer` | View active faults |
| `ChaosOperator` | View, inject and clear faults |
| `ChaosAdmin` | Everything an operator can, plus engage and release the global kill switch |

Control-plane routes accept the browser session cookie or an Entra bearer token. Unauthenticated API calls get `401`, missing roles `403`. Cookie-authenticated `POST`s must send an antiforgery token in the `X-XSRF-TOKEN` header (from `GET /api/v1/antiforgery/token`); bearer callers are exempt. Request bodies on `/api/v1/chaos` are capped at 16 KB (`413`), and reads and mutations are rate limited per user (`429` with `Retry-After`). Only `/healthz` and `/readyz` are anonymous. Pages are served with an enforced Content Security Policy; the only inline script, Blazor's import map, is admitted by a per-request nonce.

### Data-plane keys

Services that poll for directives use a separate credential: a per-service key sent in the `X-Stria-Service-Key` header. Stria stores only each key's SHA-256 (`DataPlane:ServiceKeyHashes:<ServiceName>`) and compares in constant time. A key authorizes reading only its own service's directive: no or an unknown key gets `401`, another service's directive `403`. Each route checks only its own credential type, so operator cookies and bearer tokens get `401` on the directive route, and a data-plane key gets `401` on control-plane routes. Polling is rate limited per service (`RateLimits:DataPlanePermitLimit`, default 240 per minute).

Keys cross the plain-HTTP Compose network unencrypted, which is acceptable only for local use; deployed environments need TLS and a secret store for the keys.

## HTTP API

`GET /api/v1/me` returns the caller's object ID and display name (any signed-in caller).

`GET /api/v1/chaos/faults` returns active, unexpired experiments (`ChaosViewer` or higher).

`GET /api/v1/chaos/kill-switch` returns whether the global kill switch is engaged, with who changed it, when, and why (`ChaosViewer` or higher). `POST /api/v1/chaos/kill-switch/engage` and `/release` change it (`ChaosAdmin` only) and take `{"reason":"..."}` (required, at most 500 characters); they return `200` with the new state and change nothing if it is already in that state. Engaging clears every active experiment. While engaged, `POST /api/v1/chaos/inject` with a fault returns `409 Conflict` (clearing with `"None"` still works) and services receive `204` from the directive route. Every engage and release is kept as an audit record.

`GET /api/v1/chaos/directives/{service}` returns the directive for the calling service, or `204 No Content` when it has no active fault (requires that service's data-plane key; see [Data-plane keys](#data-plane-keys)). Asking for any other target, including an unregistered one, returns `403`.

`POST /api/v1/chaos/inject` creates or replaces a target's active experiment (`ChaosOperator` or `ChaosAdmin`). Send JSON such as:

```json
{
  "targetService": "PaymentGateway",
  "faultType": "Latency",
  "latencyMs": 750,
  "durationSeconds": 120,
  "percent": 50
}
```

For an HTTP failure, use `"faultType": "HttpFailure"` and optionally set `"statusCode": 502` and `"percent": 25`. To clear a target, send `{"targetService":"PaymentGateway","faultType":"None"}`. Invalid targets, fault types, or parameter ranges return `400 Bad Request` with an RFC 7807 `application/problem+json` body.

## Sample service configuration

The sample service reads `Stria:BaseUrl`, `Stria:ServiceName`, `Stria:ServiceKey`, `Stria:PollIntervalMs`, and `Stria:MaxConcurrentDelayed` from configuration; it refuses to start without a service key. In Compose these are provided as environment variables. It polls once per second by default, keeps an existing fault lease when Stria is temporarily unreachable or rejects its key, and lets that lease expire locally rather than extending it. A rejected key is logged as a configuration error.

The Stria host reads its PostgreSQL connection string from `ConnectionStrings:Stria`. `Database:MigrateOnStartup` controls whether migrations run at startup. Retention settings are `Retention:MaxAgeDays`, `Retention:MaxClearedExperiments`, and `Retention:SweepIntervalMinutes`; expiry sweeps use `Expiry:SweepIntervalSeconds`. Data Protection keys are stored at `DataProtection:KeysPath`. Data-plane key hashes are read from `DataPlane:ServiceKeyHashes`; entries must name a registered service and hold a 64-character hex SHA-256, or startup fails.
