---
name: dotnet-azure-runtime
description: Code changes Stria (ASP.NET Core / EF Core / Npgsql) needs to run on Azure Container Apps — passwordless PostgreSQL Flexible Server via managed identity, Data Protection keys in Blob + Key Vault, forwarded headers behind ingress, and Application Insights via the Azure Monitor OpenTelemetry distro.
---

# Running Stria on Azure

Pair with `compose-to-container-apps` (infrastructure) and `azure-identity-dotnet` (credential
details). Every change below must keep `docker compose up` working: gate Azure behaviour on
configuration, not on environment names.

## 1. Passwordless PostgreSQL

Replace the connection-string-only registration with an `NpgsqlDataSource` that fetches an Entra
token as the password and refreshes it before expiry:

```csharp
var cs = builder.Configuration.GetConnectionString("Stria")!;
var useEntra = builder.Configuration.GetValue<bool>("Database:UseEntraAuth");

var dsb = new NpgsqlDataSourceBuilder(cs);
if (useEntra)
{
    var credential = new DefaultAzureCredential(); // ManagedIdentityCredential(clientId) in ACA
    var ctx = new TokenRequestContext(["https://ossrdbms-aad.database.windows.net/.default"]);
    dsb.UsePeriodicPasswordProvider(
        async (_, ct) => (await credential.GetTokenAsync(ctx, ct)).Token,
        successRefreshInterval: TimeSpan.FromMinutes(55),
        failureRefreshInterval: TimeSpan.FromSeconds(10));
}
var dataSource = dsb.Build();
builder.Services.AddSingleton(dataSource);
builder.Services.AddDbContextFactory<StriaDbContext>(o => o.UseNpgsql(dataSource));
```

- Connection string in Azure: `Host=<server>.postgres.database.azure.com;Database=stria;Username=<managed-identity-name>;SslMode=Require` — no password.
- One-time DB setup by the Entra admin: `select * from pgaadauth_create_principal('<identity-name>', false, false);` then grant only what the app needs.
- Keep the Testcontainers tests on password auth (`Database:UseEntraAuth=false`).

## 2. Data Protection across replicas

The `stria-dpkeys` volume does not exist in Container Apps, and replicas do not share disk. Without
shared keys, auth cookies and antiforgery tokens fail after a restart or on a second replica.

```bash
dotnet add Stria.API package Azure.Extensions.AspNetCore.DataProtection.Blobs
dotnet add Stria.API package Azure.Extensions.AspNetCore.DataProtection.Keys
```

```csharp
var dp = builder.Services.AddDataProtection().SetApplicationName("Stria.API");
if (builder.Configuration["DataProtection:BlobUri"] is { } blobUri)
{
    var credential = new DefaultAzureCredential();
    dp.PersistKeysToAzureBlobStorage(new Uri(blobUri), credential)
      .ProtectKeysWithAzureKeyVault(new Uri(builder.Configuration["DataProtection:KeyVaultKeyUri"]!), credential);
}
else
{
    dp.PersistKeysToFileSystem(new DirectoryInfo(builder.Configuration["DataProtection:KeysPath"] ?? "/var/lib/stria/dpkeys"));
}
```

Roles for the app identity: `Storage Blob Data Contributor` on the container, `Key Vault Crypto
Service Encryption User` on the key.

## 3. Behind Container Apps ingress

TLS terminates at ingress; the container sees HTTP with `X-Forwarded-Proto: https`.

- Add `UseForwardedHeaders` first in the pipeline, with `ForwardedHeaders.XForwardedFor |
  XForwardedProto`, enabled by a config flag. Container Apps does not give you a fixed proxy IP, so
  if you clear `KnownNetworks`/`KnownProxies`, do it only when the container is not reachable except
  through ingress (true for ACA), and document why.
- Re-test the HTTPS-redirect exemption for `/api/v1/chaos/directives` — internal callers use
  `http://stria-server` and must not be redirected.
- Probes: liveness `GET /healthz`, readiness `GET /readyz` (already safe: no config or exception
  details).

## 4. Application Insights

```bash
dotnet add Stria.API package Azure.Monitor.OpenTelemetry.AspNetCore
dotnet add Stria.API package Npgsql.OpenTelemetry
```

```csharp
if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry()
        .UseAzureMonitor()                       // reads APPLICATIONINSIGHTS_CONNECTION_STRING
        .ConfigureResource(r => r.AddService("stria-server"))
        .WithTracing(t => t.AddNpgsql().AddSource("Stria"));
}
```

- Set the service name per app (`stria-server`, `sample-payment-gateway`) so the App Insights
  application map shows them separately.
- Add an `ActivitySource("Stria")` span around fault injection and directive reads, tagged with
  target service and fault type — never with tokens or request bodies.
- While doing this, resolve the Phase 1 finding: `TrafficSimulatorService` logs active faults at
  `Critical`; that will page someone once alerts exist. Lower it or remove the service.
- Optional: `UseAzureMonitor(o => o.Credential = credential)` for Entra-authenticated ingestion
  (needs `Monitoring Metrics Publisher` on the App Insights resource).

## 5. Migrations

Keep `Database:MigrateOnStartup=false` in Azure. Build an EF bundle in CI
(`dotnet ef migrations bundle --project Stria.API --self-contained -r linux-x64 -o efbundle`) and run
it in a Container Apps job with the same managed identity before the new revision goes live.
