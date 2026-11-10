# Stria

Stria is a .NET 10 chaos engineering demo. It provides a web dashboard and HTTP API for creating time-limited fault experiments against registered services, stores experiment history in PostgreSQL, and serves active fault directives to participating services.

## Current capabilities

Stria recognizes three target names: `PaymentGateway`, `AuthService`, and `InventoryAPI`. Fault types accepted by the API are:

- `Latency`: adds a delay of 1–60,000 ms to a selected percentage of requests.
- `HttpFailure`: returns a selected HTTP status in the 500–599 range to a selected percentage of requests.
- `CpuSpike`: records an experiment, but the sample service does not execute this fault.
- `None`: clears the active fault for the target.

Experiments have a lease from 1 to 3,600 seconds, defaulting to 300 seconds. Requests that omit the percentage use 100%; `HttpFailure` defaults to status 503. The server records the initiating identity as `anonymous` until authentication is configured; a caller-supplied `TriggeredBy` value is ignored.

The dashboard is served by the Stria ASP.NET Core host with interactive Blazor components. PostgreSQL stores experiment state. The `sample-payment-gateway` container polls Stria for `PaymentGateway` directives and applies latency or HTTP failure faults to its `/work` endpoint. Its `/healthz` endpoint remains available while faults are active. The database is only exposed on the internal Compose network; the sample service and Stria host ports are bound to loopback.

## Run with Docker Compose

Create a `.env` file beside `compose.yaml` containing a local database password:

```dotenv
STRIA_DB_PASSWORD=replace-with-a-strong-local-password
```

The `.env` file is used by Compose to configure PostgreSQL and Stria's connection string. Then, from the repository root, run:

```sh
docker compose up --build
```

Compose starts PostgreSQL, waits for its health check, starts Stria, and starts the sample service. In this configuration, Stria applies database migrations on startup. The ports are:

- `https://localhost:8443` — Stria dashboard and API (the local HTTPS certificate must be trusted by your browser).
- `http://localhost:8081/work` — sample service work endpoint.
- `http://localhost:8081/healthz` — sample service health endpoint.

Stop the stack with `Ctrl+C`; use `docker compose down` to stop and remove the containers. The named `stria-pgdata` volume retains database contents when containers are removed.

## HTTP API

`GET /api/chaos/faults` returns active, unexpired experiments.

`GET /api/chaos/directives/{service}` returns the directive for a registered service, or `204 No Content` when it has no active fault. An unknown target returns `404`.

`POST /api/chaos/inject` creates or replaces a target's active experiment. Send JSON such as:

```json
{
  "targetService": "PaymentGateway",
  "faultType": "Latency",
  "latencyMs": 750,
  "durationSeconds": 120,
  "percent": 50
}
```

For an HTTP failure, use `"faultType": "HttpFailure"` and optionally set `"statusCode": 502` and `"percent": 25`. To clear a target, send `{"targetService":"PaymentGateway","faultType":"None"}`. Invalid targets, fault types, or parameter ranges return `400 Bad Request`.

## Sample service configuration

The sample service reads `Stria:BaseUrl`, `Stria:ServiceName`, `Stria:PollIntervalMs`, and `Stria:MaxConcurrentDelayed` from configuration. In Compose these are provided as environment variables. It polls once per second by default, keeps an existing fault lease when Stria is temporarily unreachable, and lets that lease expire locally rather than extending it.

The Stria host reads its PostgreSQL connection string from `ConnectionStrings:Stria`. `Database:MigrateOnStartup` controls whether migrations run at startup. Retention settings are `Retention:MaxAgeDays`, `Retention:MaxClearedExperiments`, and `Retention:SweepIntervalMinutes`; expiry sweeps use `Expiry:SweepIntervalSeconds`.
