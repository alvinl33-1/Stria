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

## CI and quality gates

GitHub Actions runs on pushes and pull requests to `main`:

| Job | What it proves |
|---|---|
| `build-and-test` | Formatting is clean, the Release build has no warnings, all tests pass, and TRX/coverage evidence is uploaded |
| `dependency-audit` | No known vulnerable direct or transitive NuGet packages are referenced |
| `dependency-review` | A pull request does not introduce high-severity vulnerable dependencies (public repositories; private repositories need GitHub Advanced Security) |
| `container-build` | Builds the control-plane, migration-job, standalone dashboard and sample-service images for `linux/amd64`, tagged `sha-<commit>` (never `latest`). The control-plane image starts in Production mode and serves the Blazor script its pages reference; the migrations bundle brings an empty PostgreSQL to the latest migration and is a no-op on a second run; Trivy fails the job on fixable HIGH/CRITICAL findings in any image (accepted findings go in `.trivyignore`) |

Integration tests use Testcontainers and PostgreSQL. CI sets `STRIA_REQUIRE_DOCKER_TESTS=true`, so the job fails rather than silently skipping PostgreSQL coverage when Docker cannot start. Dependabot checks NuGet packages, GitHub Actions, and Docker base images weekly.

Run the same .NET gates locally with:

```sh
dotnet restore Stria.sln
dotnet format Stria.sln --verify-no-changes
dotnet build Stria.sln --configuration Release --no-restore -warnaserror
dotnet test Stria.sln --configuration Release --no-build
```

`main` branch protection requires `build-and-test`, `dependency-audit`, `dependency-review`, and `container-build`. `dependency-review` needs a public repository (or GitHub Advanced Security); drop it from the required checks if the repository becomes private. Administrators can bypass these rules; do not use that bypass for normal changes.

The Phase 2 security plan is in [docs/PHASE_2_PLAN.md](docs/PHASE_2_PLAN.md); its evidence is in [docs/PHASE_2_HANDOFF.md](docs/PHASE_2_HANDOFF.md). Phase 3 (hosted demo on Azure Container Apps) is planned in [docs/PHASE_3_PLAN.md](docs/PHASE_3_PLAN.md), with one figure per sprint in [docs/PHASE_3_SPRINTS.md](docs/PHASE_3_SPRINTS.md); the infrastructure is described, not yet provisioned, in `infra/`.

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
