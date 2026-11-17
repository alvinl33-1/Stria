# Stria — Compose → Container Apps map

Source of truth: `compose.yaml`, the Dockerfiles, and `docs/PHASE_2_PLAN.md`.

**Phase guard:** Phase 2 says *do not provision Azure resources or incur subscription cost* until
Phase 3 is explicitly authorized, and control/directive endpoints must be authenticated before
public ingress. Until then, this skill may only produce files (Bicep, `azure.yaml`, workflow
drafts) and `what-if` output — no `azd up`, no `az deployment … create`.

| Compose service | Azure target | Ingress | Scale | Notes |
|---|---|---|---|---|
| `stria-server` (`Stria.API/Dockerfile`, port 8080) | Container app `stria-server` | external **only after Phase 2 auth ships**; internal before that | 1–2 | Probes: liveness `/healthz`, readiness `/readyz`. Set `Database__MigrateOnStartup=false`. |
| `sample-payment-gateway` | Container app `sample-payment-gateway` | internal | 1 | `Stria__BaseUrl=http://stria-server`. Authenticates to the directive API with its own managed identity (Phase 2 data-plane credential). |
| `Stria.Client` standalone image | not deployed by default | — | — | The host already serves the dashboard. Deploy separately only if you split the UI. |
| Migrations | Container Apps job `stria-migrate` | — | manual | Build an EF migration bundle (`dotnet ef migrations bundle --project Stria.API -o efbundle`) into a small image; CI runs it before updating `stria-server`. This is the "controlled release migration" Phase 3 calls for. |
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
  `X-Forwarded-Proto: https`. Add `UseForwardedHeaders` (trusting the ACA proxy) before
  `UseHttpsRedirection`/HSTS, and re-test the exemption.
- The sample service keeps a lease while Stria is unreachable — this is what makes rolling
  revisions safe. Keep it.
