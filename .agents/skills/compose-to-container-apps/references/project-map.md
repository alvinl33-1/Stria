# Stria — Compose → Container Apps map

Source of truth: `compose.yaml`, the Dockerfiles, `infra/`, and `docs/PHASE_3_PLAN.md`.

**Phase guard:** `docs/PHASE_3_PLAN.md` Step 4 onward (provisioning, deploys) needs Alvin's written
authorization with a region, a monthly budget and a teardown date. Until then this skill may only
produce files (Bicep, `azure.yaml`, workflow drafts) and offline `az bicep build`/`lint` output —
no `azd up`, no `az deployment … create`. Control and directive endpoints are authenticated
(Phase 2), so `stria-server` may have external ingress.

| Compose service | Azure target | Ingress | Scale | Notes |
|---|---|---|---|---|
| `stria-server` (`Stria.API/Dockerfile`, port 8080) | Container app `stria-server` | external (HTTPS) | 0–1 (max 1: rate limits are in memory) | Probes: liveness `/healthz`, readiness `/readyz`. Leave `Database__MigrateOnStartup` unset. Set `ForwardedHeaders__Enabled=true` and the other Azure settings in the plan's Step 1 table. |
| `sample-payment-gateway` | Container app `sample-payment-gateway` | internal | 1 | `Stria__BaseUrl=http://stria-server`. Authenticates to the directive API with its per-service data-plane key (`Stria__ServiceKey`, a Key Vault secret); Stria holds only its SHA-256 (`DataPlane__ServiceKeyHashes__PaymentGateway`). |
| `Stria.Client` standalone image | not deployed by default | — | — | The host already serves the dashboard. Deploy separately only if you split the UI. |
| Migrations | Container Apps job `stria-migrate` | — | manual | Image `stria-migrator` (`Stria.API/Dockerfile --target migrator`): a self-contained EF bundle configured by `ConnectionStrings__Stria` and `Database__UseEntraAuth`. CI already builds it and tests it against PostgreSQL; the Sprint 5 deploy job runs it before updating `stria-server`. It needs DDL rights, so decide in Sprint 4 whether it gets its own database role. |
| `db` (postgres:16) | PostgreSQL Flexible Server 16 | private/firewalled | — | Entra auth with Npgsql's periodic password provider; see `dotnet-azure-runtime`. |
| `stria-dpkeys` volume | Blob container + Key Vault key | — | — | Replicas cannot share local disk. Use `PersistKeysToAzureBlobStorage` + `ProtectKeysWithAzureKeyVault` (packages `Azure.Extensions.AspNetCore.DataProtection.Blobs` / `.Keys`). Otherwise antiforgery tokens and auth cookies break across replicas and restarts. |
| `stria-pgdata` volume | Flexible Server storage | — | — | Turn on automated backups; note the retention in the README. |

## Configuration

| Setting | Azure source |
|---|---|
| `ConnectionStrings__Stria` | Host/db/user only — no password; token supplied by the data source |
| `AzureAd__*` (Phase 2) | Plain env vars for tenant/client IDs; client secret (if any) from Key Vault — prefer a federated credential / managed identity |
| `DataProtection__*` | Blob URI + Key Vault key URI as env vars |
| `Retention__*`, `Expiry__*` | Plain env vars |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Env var from the App Insights resource |

## Existing behaviour to preserve

- The directive endpoint is exempt from HTTPS redirect for the internal plain-HTTP listener. In
  Container Apps, TLS ends at ingress and traffic reaches the container over HTTP with
  `X-Forwarded-Proto: https`. `UseForwardedHeaders` runs first when `ForwardedHeaders__Enabled=true`
  (Sprint 1, covered by `ForwardedHeadersTests`).
- The sample service keeps a lease while Stria is unreachable — this is what makes rolling
  revisions safe. Keep it.
