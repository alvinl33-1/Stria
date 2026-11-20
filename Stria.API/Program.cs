// Stria.API/Program.cs

using Azure.Core;
using Azure.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Stria.Client.Services;
using Stria.Components;
using Stria.Data;
using Stria.Endpoints;
using Stria.Security;
using Stria.Services;

var builder = WebApplication.CreateBuilder(args);

// Refuse to start half-configured. Identity, data-plane keys, the Azure set and the database
// settings are all checked here, before anything is registered.
StartupValidation.ThrowIfInvalid(builder.Configuration, builder.Environment);

builder.Services.AddProblemDetails();

// Services
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents()
    .AddAuthenticationStateSerialization();
builder.Services.AddCascadingAuthenticationState();

// Components are prerendered on the server before WebAssembly takes over, so the services they
// inject must exist here too. Prerendering never calls the API (polling starts in
// OnAfterRender), so this server-side HttpClient never needs the user's credentials.
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(sp.GetRequiredService<NavigationManager>().BaseUri)
});
builder.Services.AddScoped<ChaosApiClient>();

// Azure resources (Blob, Key Vault, PostgreSQL) are reached with the container app's managed
// identity. Created only if one of them is configured, so Compose and tests never build it.
var azureCredential = new Lazy<TokenCredential>(() => new DefaultAzureCredential());

builder.Services.AddStriaPersistence(builder.Configuration, azureCredential);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<ExperimentRetentionOptions>(
    builder.Configuration.GetSection(ExperimentRetentionOptions.SectionName));
builder.Services.Configure<ExperimentExpiryOptions>(
    builder.Configuration.GetSection(ExperimentExpiryOptions.SectionName));

builder.Services.AddSingleton<IChaosOrchestrator, DatabaseChaosOrchestrator>();
builder.Services.AddHostedService<ExperimentRetentionService>();
builder.Services.AddHostedService<ExperimentExpiryService>();

builder.Services.AddStriaDataProtection(builder.Configuration, azureCredential);
builder.Services.AddStriaAuthentication(builder.Configuration);
builder.Services.AddStriaAuthorization();

// Cookie-authenticated JSON mutations send the antiforgery token in this header.
builder.Services.AddAntiforgery(options => options.HeaderName = "X-XSRF-TOKEN");
builder.Services.AddChaosRateLimiting(builder.Configuration);
builder.Services.AddStriaTelemetry(builder.Configuration);

// Behind Azure Container Apps ingress, TLS ends at the proxy and the original scheme and client
// IP arrive in X-Forwarded-* headers. Without them the OIDC redirect_uri is built as http:// and
// sign-in fails. Off by default: Compose has no proxy, and trusting these headers there would
// let any caller spoof them.
var forwardedHeadersEnabled = builder.Configuration.GetValue<bool>(AzureRuntimeSettings.ForwardedHeadersKey);
if (forwardedHeadersEnabled)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        // The ingress IPs aren't fixed, and the container is reachable only through the
        // ingress, so trust whichever proxy is in front of it.
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

var app = builder.Build();

// Migrations are opt-in (Database:MigrateOnStartup=true), e.g. for local dev / Compose.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    var dbFactory = app.Services.GetRequiredService<IDbContextFactory<StriaDbContext>>();
    await using var db = await dbFactory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
}

// 2. MIDDLEWARE ROUTING PIPELINE
// First, so HSTS, HTTPS redirection, the cookie Secure flag and the OIDC redirect_uri all see
// the scheme the browser actually used.
if (forwardedHeadersEnabled)
    app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// Data-plane services poll /api/v1/chaos/directives on the plain-HTTP listener (8080; Compose
// publishes it to the host on loopback only). Their keys cross this hop unencrypted, which is
// acceptable on the local Compose network only; Phase 3 terminates TLS at the ingress.
app.UseWhen(
    ctx => !ctx.Request.Path.StartsWithSegments("/api/v1/chaos/directives"),
    branch => branch.UseHttpsRedirection());
app.UseChaosBodyLimit();
app.UseSecurityHeaders();
app.UseAuthentication();
app.UseAuthorization();
// After UseAuthorization: only then is HttpContext.User populated for bearer callers, so the
// limiter can partition per user instead of lumping every client behind one IP.
app.UseRateLimiter();
app.UseAntiforgery();
app.UseStatusCodePages();

// 3. ENDPOINTS
// Each module attaches its own authorization, rate-limit and antiforgery metadata; the
// middleware above enforces it. Both API modules live under one versioned prefix.
app.MapHealthEndpoints();
app.MapAuthenticationEndpoints();
var api = app.MapGroup("/api/v1");
api.MapControlPlaneEndpoints();
api.MapDataPlaneEndpoints();

// 4. MAP ROOT UI PATHS
// Static assets are endpoints in .NET 10: MapStaticAssets serves _framework/blazor.web.js and the
// fingerprinted paths that @Assets[...] resolves (UseStaticFiles does neither). AllowAnonymous
// keeps the fallback sign-in policy off CSS/JS, so pages such as /access-denied still load.
app.MapStaticAssets().AllowAnonymous();

app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Stria.Client._Imports).Assembly);

app.Run();

public partial class Program { }
