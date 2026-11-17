---
name: aspnet-entra-auth
description: Implement Stria's Phase 2 Microsoft Entra ID authentication and authorization in ASP.NET Core — Microsoft.Identity.Web cookie sign-in for the Blazor host, JWT bearer for API clients, a separate data-plane scheme, ChaosViewer/Operator/Admin policies, antiforgery, rate limits, and authorization-matrix tests.
---

# Entra ID auth for Stria (Phase 2)

`docs/PHASE_2_PLAN.md` is the spec. This skill is the implementation guide; when the two disagree,
the plan wins — point out the conflict instead of silently choosing.

**Scope guard:** use a test Entra tenant or a local OIDC provider. Do not provision Azure resources.

## Packages

```bash
dotnet add Stria.API package Microsoft.Identity.Web
dotnet add Stria.API package Microsoft.Identity.Web.UI   # only if you use its sign-in/out pages
```

Pin exact versions like the other `PackageReference`s; the CI vulnerability audit covers them.

## Schemes — keep them explicit

| Caller | Scheme | Registration |
|---|---|---|
| Operator in browser | `OpenIdConnect` + cookie | `AddMicrosoftIdentityWebApp(config.GetSection("AzureAd"))` |
| Automation / CLI | `Bearer` (control plane audience) | `AddMicrosoftIdentityWebApi(config.GetSection("AzureAdApi"), JwtBearerDefaults.AuthenticationScheme)` |
| Data plane (sample service) | `DataPlane` (own audience or hashed per-service key) | separate `AddMicrosoftIdentityWebApi(..., "DataPlane")` or a custom handler |

Do **not** rely on a default scheme to satisfy policies. Attach schemes on endpoint groups:

```csharp
var control = app.MapGroup("/api/v1/chaos")
    .RequireAuthorization(new AuthorizeAttribute
    {
        AuthenticationSchemes = $"{OpenIdConnectDefaults.AuthenticationScheme},{JwtBearerDefaults.AuthenticationScheme}"
    });

control.MapGet("/faults", ...).RequireAuthorization("Chaos.View");
control.MapPost("/inject", ...).RequireAuthorization("Chaos.Operate");

app.MapGet("/api/v1/chaos/directives/{service}", ...)
   .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "DataPlane", Policy = "Chaos.DataPlane" });
```

With cookie auth, an unauthenticated API call should get `401`, not a redirect to the sign-in page —
handle `OnRedirectToLogin` for `/api` paths.

## Policies

```csharp
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Chaos.View",    p => p.RequireRole("ChaosViewer", "ChaosOperator", "ChaosAdmin"))
    .AddPolicy("Chaos.Operate", p => p.RequireRole("ChaosOperator", "ChaosAdmin"))
    .AddPolicy("Chaos.Admin",   p => p.RequireRole("ChaosAdmin"))
    .AddPolicy("Chaos.DataPlane", p => p.AddRequirements(new OwnDirectiveRequirement()));
```

Entra puts app roles in the `roles` claim. Confirm `RequireRole` sees them with a test; if not, set
`TokenValidationParameters.RoleClaimType = "roles"` for that scheme. Never authorize on display name
or email.

`OwnDirectiveRequirement`'s handler compares the route `{service}` with the caller's service
identity claim and fails otherwise — a service reads only its own directive.

## Trusted actor

Replace `http.User.Identity?.Name ?? "anonymous"` with the `oid` claim
(`http://schemas.microsoft.com/identity/claims/objectidentifier` or `oid`), falling back to `sub` /
`NameIdentifier`. Keep display name in a separate nullable column (needs a migration and a
PostgreSQL integration test).

## CSRF, limits, headers

- `UseAntiforgery()` is already in the pipeline. Minimal-API JSON endpoints are **not** validated
  automatically: for cookie-authenticated `POST`s call `IAntiforgery.ValidateRequestAsync(http)`
  (endpoint filter), and have the dashboard send the token header configured with
  `AddAntiforgery(o => o.HeaderName = "X-XSRF-TOKEN")`. Skip validation only for bearer-token callers.
- Body limit (~16 KB) on `/api/v1/chaos/*` via an endpoint filter or `RequestSizeLimit`.
- `AddRateLimiter` with partitioned policies: per user/IP for reads and mutations, per service
  identity for data-plane polling; return `429` + `Retry-After` + problem details.
- `UseForwardedHeaders` only with explicit `KnownProxies`/`KnownNetworks` (in Azure: the Container
  Apps ingress).

## Sample service

Phase 2 locally: per-service hashed key from configuration (constant-time compare). Phase 3 on Azure:
the sample service's managed identity requests a token for Stria's data-plane app ID URI
(`DefaultAzureCredential` / `ManagedIdentityCredential`, see `azure-identity-dotnet`), and the
managed identity is assigned the data-plane app role.

## Tests (must land with the code)

Extend `Stria.Tests/TestSupport/StriaApiFactory.cs` with a test auth handler that issues principals
with chosen roles/claims. Cover the matrix:

| Caller | GET faults | POST inject | kill switch | own directive | other directive |
|---|---|---|---|---|---|
| anonymous | 401 | 401 | 401 | 401 | 401 |
| ChaosViewer | 200 | 403 | 403 | 401 | 401 |
| ChaosOperator | 200 | 202 | 403 | 401 | 401 |
| ChaosAdmin | 200 | 202 | 200 | 401 | 401 |
| data plane (PaymentGateway) | 401 | 401 | 401 | 200/204 | 403 |

Plus: cookie `POST` without antiforgery token → 400; operator bearer token on directive route → 401;
`TriggeredBy` in JSON is ignored.
