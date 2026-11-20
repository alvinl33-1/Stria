// Stria.API/Program.cs

using Azure.Core;
using Azure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Stria.Client.Security;
using Stria.Client.Services;
using Stria.Components;
using Stria.Data;
using Stria.Endpoints;
using Stria.Security;
using Stria.Services;

var builder = WebApplication.CreateBuilder(args);
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

// Persistence. A DbContext *factory* (not a scoped DbContext) lets the singleton orchestrator
// and the hosted services create a short-lived context per operation.
var connectionString = builder.Configuration.GetConnectionString("Stria")
    ?? throw new InvalidOperationException(
        "Connection string 'Stria' is not configured. Set ConnectionStrings__Stria (never commit credentials).");

// Azure resources (Blob, Key Vault, PostgreSQL) are reached with the container app's managed
// identity. Created only if one of them is configured, so Compose and tests never build it.
var azureCredential = new Lazy<TokenCredential>(() => new DefaultAzureCredential());

// Identity settings. Outside Development a missing value stops startup instead of running with
// broken sign-in. There is deliberately no switch that turns authentication off.
if (!builder.Environment.IsDevelopment())
{
    string[] required =
    [
        "AzureAd:TenantId", "AzureAd:ClientId", "AzureAd:ClientSecret",
        "AzureAdApi:TenantId", "AzureAdApi:ClientId"
    ];
    var missing = required.Where(key => string.IsNullOrWhiteSpace(builder.Configuration[key])).ToArray();
    if (missing.Length > 0)
        throw new InvalidOperationException(
            $"Identity settings missing: {string.Join(", ", missing)}. Use user-secrets or environment variables.");

    if (!builder.Configuration.GetSection("DataPlane:ServiceKeyHashes").GetChildren().Any())
        throw new InvalidOperationException(
            "DataPlane:ServiceKeyHashes has no entries, so no service could read its directives.");
}

// Azure settings, in every environment: a half-configured deployment must not start.
var missingAzure = AzureRuntimeSettings.Missing(builder.Configuration);
if (missingAzure.Count > 0)
    throw new InvalidOperationException(
        $"Azure settings incomplete: {string.Join(", ", missingAzure)}. Set all of them, or none for local use.");
if (builder.Configuration.GetValue<bool>(StriaDataSource.UseEntraAuthKey) && AzureRuntimeSettings.HasPassword(connectionString))
    throw new InvalidOperationException(
        $"{StriaDataSource.UseEntraAuthKey} is on, so ConnectionStrings:Stria must not contain a password.");

var dataSource = StriaDataSource.Create(
    connectionString, builder.Configuration.GetValue<bool>(StriaDataSource.UseEntraAuthKey), azureCredential);
builder.Services.AddSingleton(dataSource);
builder.Services.AddDbContextFactory<StriaDbContext>(options => options.UseNpgsql(dataSource));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<ExperimentRetentionOptions>(
    builder.Configuration.GetSection(ExperimentRetentionOptions.SectionName));
builder.Services.Configure<ExperimentExpiryOptions>(
    builder.Configuration.GetSection(ExperimentExpiryOptions.SectionName));

builder.Services.AddSingleton<IChaosOrchestrator, DatabaseChaosOrchestrator>();
builder.Services.AddHostedService<ExperimentRetentionService>();
builder.Services.AddHostedService<ExperimentExpiryService>();

builder.Services.AddStriaDataProtection(builder.Configuration, azureCredential);

// Authentication.
//   Browser operators: OpenID Connect (authorization-code flow) + an encrypted cookie.
//   Automation/CLI:    JWT bearer tokens issued for this API's audience.
// Pages use the cookie by default. Every API route names its schemes explicitly instead of
// relying on a default (see ControlPlane below).
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));
builder.Services.AddAuthentication()
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAdApi"), JwtBearerDefaults.AuthenticationScheme);

// Data plane: polling services authenticate with their own key, never an operator credential.
builder.Services.AddOptions<DataPlaneOptions>()
    .Bind(builder.Configuration.GetSection(DataPlaneOptions.SectionName))
    .Validate(DataPlaneKeys.IsValid,
        "DataPlane:ServiceKeyHashes entries must name a registered service and hold a 64-character hex SHA-256.")
    .ValidateOnStart();
builder.Services.AddAuthentication()
    .AddScheme<AuthenticationSchemeOptions, DataPlaneAuthenticationHandler>(
        DataPlaneDefaults.AuthenticationScheme, configureOptions: null);
builder.Services.AddSingleton<IAuthorizationHandler, OwnDirectiveHandler>();

builder.Services.Configure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
{
    options.ResponseType = OpenIdConnectResponseType.Code;
    options.MapInboundClaims = false; // keep Entra's short claim names ("oid", "roles", "name")
    options.TokenValidationParameters.NameClaimType = "name";
    options.TokenValidationParameters.RoleClaimType = "roles";

    // An unauthenticated call to an /api route without explicit schemes must get 401, not a
    // redirect to the Microsoft sign-in page.
    var redirect = options.Events.OnRedirectToIdentityProvider;
    options.Events.OnRedirectToIdentityProvider = ctx =>
    {
        if (!IsApi(ctx.Request))
            return redirect(ctx);
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        ctx.HandleResponse();
        return Task.CompletedTask;
    };
});

// Microsoft.Identity.Web installs its own JsonWebTokenHandler, and the MapInboundClaims setter
// above only reaches the framework's default handlers. Turn mapping off on the handler that
// actually validates the ID token; otherwise Entra's "roles" claim is renamed to ClaimTypes.Role,
// RoleClaimType "roles" never matches, and every signed-in user is denied.
builder.Services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
{
    if (options.TokenHandler is JsonWebTokenHandler handler)
        handler.MapInboundClaims = false;
});

builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
{
    options.MapInboundClaims = false;
    options.TokenValidationParameters.NameClaimType = "name";
    options.TokenValidationParameters.RoleClaimType = "roles";
});

builder.Services.PostConfigure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.AccessDeniedPath = "/access-denied";

    // API callers get status codes; only page requests are redirected.
    var toLogin = options.Events.OnRedirectToLogin;
    var toDenied = options.Events.OnRedirectToAccessDenied;
    options.Events.OnRedirectToLogin = ctx =>
        IsApi(ctx.Request) ? SetStatus(ctx.Response, StatusCodes.Status401Unauthorized) : toLogin(ctx);
    options.Events.OnRedirectToAccessDenied = ctx =>
        IsApi(ctx.Request) ? SetStatus(ctx.Response, StatusCodes.Status403Forbidden) : toDenied(ctx);
});

builder.Services.AddAuthorization(options =>
{
    ChaosPolicies.AddPolicies(options);
    // Secure by default: an endpoint without its own authorization metadata still requires a
    // signed-in user. Public endpoints opt out explicitly with AllowAnonymous().
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    options.AddPolicy(DataPlaneDefaults.Policy, policy => policy
        .AddAuthenticationSchemes(DataPlaneDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .AddRequirements(new OwnDirectiveRequirement()));
});

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

static bool IsApi(HttpRequest request) => request.Path.StartsWithSegments("/api");

static Task SetStatus(HttpResponse response, int statusCode)
{
    response.StatusCode = statusCode;
    return Task.CompletedTask;
}

public partial class Program { }
