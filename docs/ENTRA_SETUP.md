# Microsoft Entra setup (local development)

Stria signs operators in with Microsoft Entra ID. Phase 2 uses a free **test tenant** only; no
Azure subscription or paid resources are needed. Nothing below is committed: tenant and client IDs
go in user-secrets or `.env`, and the client secret is a secret.

## 1. App registration

In the Entra admin center for your test tenant:

1. **App registrations → New registration**: name `Stria (local)`, single tenant.
2. **Authentication → Web redirect URIs**:
   - `https://localhost:7248/signin-oidc` (`dotnet run`, https profile)
   - `http://localhost:8080/signin-oidc` (Docker Compose; Entra allows `http` only for `localhost`)
   - Optional: add `…/signout-callback-oidc` for each host so sign-out returns to Stria instead
     of stopping on Microsoft's "signed out" page.
3. **Certificates & secrets → New client secret**. Copy the value immediately.
4. **App roles** — values must match exactly:

   | Display name | Value | Allowed member types |
   |---|---|---|
   | Chaos Viewer | `ChaosViewer` | Users/Groups |
   | Chaos Operator | `ChaosOperator` | Both (users and applications) |
   | Chaos Admin | `ChaosAdmin` | Users/Groups |

5. **Expose an API**: accept `api://<client-id>` and add the scope `Chaos.Access`.
6. **Enterprise applications → Stria (local) → Users and groups**: assign each test user one
   role. Assign users directly; group assignment needs Entra ID P1. Recommended: set
   **Properties → Assignment required = Yes** so users without a role cannot sign in.

## 2. Local configuration

`dotnet run` (Development) reads user-secrets:

```sh
dotnet user-secrets set "AzureAd:TenantId"     "<tenant-id>" --project Stria.API
dotnet user-secrets set "AzureAd:ClientId"     "<client-id>" --project Stria.API
dotnet user-secrets set "AzureAd:ClientSecret" "<secret>"    --project Stria.API
dotnet user-secrets set "AzureAdApi:TenantId"  "<tenant-id>" --project Stria.API
dotnet user-secrets set "AzureAdApi:ClientId"  "<client-id>" --project Stria.API
```

Docker Compose runs Stria as **Production** and reads `.env` (see `.env.example`):

```dotenv
STRIA_TENANT_ID=<tenant-id>
STRIA_CLIENT_ID=<client-id>
STRIA_CLIENT_SECRET=<secret>
```

Outside Development the host refuses to start if any identity setting is missing. There is no
setting that disables authentication.

## 3. Calling the API as automation

Control-plane routes accept the browser cookie or a bearer token for `api://<client-id>`. An
application granted the `ChaosOperator` app role can use the client-credentials flow with scope
`api://<client-id>/.default`. Bearer callers do not need an antiforgery token; cookie callers do.

## 4. Checks after setup

- Open the dashboard: you are sent to Microsoft sign-in and returned to Stria.
- `GET /api/v1/me` in the same browser returns your object ID.
- Viewer: the faults table is visible but no inject/clear buttons; a `POST` to
  `/api/v1/chaos/inject` returns `403`.
- Operator: inject and clear work; the stored `TriggeredBy` is your object ID.
