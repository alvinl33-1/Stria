// Stria.API/Program.cs

using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Npgsql;
using Stria.Client.Security;
using Stria.Client.Services;
using Stria.Components;
using Stria.Data;
using Stria.Domain;
using Stria.DTOs;
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
}

builder.Services.AddDbContextFactory<StriaDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<ExperimentRetentionOptions>(
    builder.Configuration.GetSection(ExperimentRetentionOptions.SectionName));
builder.Services.Configure<ExperimentExpiryOptions>(
    builder.Configuration.GetSection(ExperimentExpiryOptions.SectionName));

builder.Services.AddSingleton<IChaosOrchestrator, DatabaseChaosOrchestrator>();
builder.Services.AddHostedService<ExperimentRetentionService>();
builder.Services.AddHostedService<ExperimentExpiryService>();

builder.Services.AddDataProtection()
    .SetApplicationName("Stria.API")
    .PersistKeysToFileSystem(new DirectoryInfo(
        builder.Configuration["DataProtection:KeysPath"] ?? "/var/lib/stria/dpkeys"));

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
});

// Cookie-authenticated JSON mutations send the antiforgery token in this header.
builder.Services.AddAntiforgery(options => options.HeaderName = "X-XSRF-TOKEN");
builder.Services.AddChaosRateLimiting(builder.Configuration);

var app = builder.Build();

// Migrations are opt-in (Database:MigrateOnStartup=true), e.g. for local dev / Compose.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    var dbFactory = app.Services.GetRequiredService<IDbContextFactory<StriaDbContext>>();
    await using var db = await dbFactory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
}

// 2. MIDDLEWARE ROUTING PIPELINE
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// Data-plane services poll /api/v1/chaos/directives over the private Compose network on the
// plain-HTTP listener (8080, not published to the host). Without this exemption the HTTPS
// redirect would bounce them to a port whose dev certificate they don't trust.
app.UseWhen(
    ctx => !ctx.Request.Path.StartsWithSegments("/api/v1/chaos/directives"),
    branch => branch.UseHttpsRedirection());
app.UseStaticFiles();
app.UseChaosBodyLimit();
app.UseSecurityHeaders();
app.UseAuthentication();
app.UseAuthorization();
// After UseAuthorization: only then is HttpContext.User populated for bearer callers, so the
// limiter can partition per user instead of lumping every client behind one IP.
app.UseRateLimiter();
app.UseAntiforgery();
app.UseStatusCodePages();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapGet("/readyz", async (
    IDbContextFactory<StriaDbContext> dbFactory,
    ILogger<Program> logger,
    CancellationToken ct) =>
{
    try
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Database.CanConnectAsync(ct)
            ? Results.Ok(new { status = "ready" })
            : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
    catch (NpgsqlException ex)
    {
        logger.LogWarning(ex, "Readiness check could not connect to PostgreSQL.");
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
}).AllowAnonymous();

// Browser sign-in/out. Sign-out is a POST so a cross-site link cannot log an operator out;
// its form field binding makes UseAntiforgery() validate the token automatically.
var authentication = app.MapGroup("/authentication");
authentication.MapGet("/login", (string? returnUrl) => TypedResults.Challenge(
        new AuthenticationProperties { RedirectUri = LocalUrl(returnUrl) },
        [OpenIdConnectDefaults.AuthenticationScheme]))
    .AllowAnonymous();
authentication.MapPost("/logout", ([FromForm] string? returnUrl) => TypedResults.SignOut(
    new AuthenticationProperties { RedirectUri = LocalUrl(returnUrl) },
    [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]));

var api = app.MapGroup("/api/v1");

api.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new
{
    oid = user.GetObjectId(),
    name = user.FindFirstValue("name")
}))
    .RequireAuthorization(ControlPlane(policy: null))
    .RequireRateLimiting(HttpHardening.ReadPolicy);

api.MapGet("/antiforgery/token", (IAntiforgery antiforgery, HttpContext http) =>
    {
        http.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new { token = antiforgery.GetAndStoreTokens(http).RequestToken });
    })
    .RequireAuthorization(ControlPlane(ChaosPolicies.View))
    .RequireRateLimiting(HttpHardening.ReadPolicy);

api.MapGet("/chaos/faults", async (IChaosOrchestrator orchestrator, CancellationToken ct) =>
        Results.Ok(await orchestrator.GetAllActiveFaultsAsync(ct)))
    .RequireAuthorization(ControlPlane(ChaosPolicies.View))
    .RequireRateLimiting(HttpHardening.ReadPolicy);

// Data-plane feed: what should service {service} be doing right now?
//   200 + FaultDirective  -> apply it for RemainingSeconds
//   204                   -> no active fault
//   404                   -> not a registered service
// Anonymous until Phase 2 Step 4 adds the data-plane credential.
api.MapGet("/chaos/directives/{service}", async (
    string service,
    IChaosOrchestrator orchestrator,
    TimeProvider time,
    CancellationToken ct) =>
{
    if (!ChaosRules.TryGetService(service, out var canonical))
        return Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Unknown target service.",
            type: "about:blank");

    var experiment = await orchestrator.GetActiveFaultForServiceAsync(canonical, ct);
    if (experiment is null)
        return Results.NoContent();

    // Hand out a duration, not a timestamp: the consumer counts it down on its own monotonic
    // clock, so clock skew between machines can't stretch or shorten the fault.
    var remaining = (int)Math.Ceiling((experiment.ExpiresAt - time.GetUtcNow().UtcDateTime).TotalSeconds);
    if (remaining < 1)
        return Results.NoContent(); // expired between the query and now

    return Results.Ok(new FaultDirective(
        experiment.Id,
        experiment.FaultType,
        experiment.LatencyMs,
        experiment.Percent,
        experiment.StatusCode,
        remaining));
}).AllowAnonymous();

api.MapPost("/chaos/inject", async (
        FaultInjectionRequest request,
        HttpContext http,
        IChaosOrchestrator orchestrator,
        CancellationToken ct) =>
    {
        // Validation (targets, fault types, parameter bounds) lives in ChaosRules and is
        // re-enforced inside the orchestrator.
        if (!ChaosRules.TryValidate(request, out _, out var error))
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid fault injection request.",
                detail: error,
                type: "about:blank");

        // The actor comes from validated claims; the request's TriggeredBy is ignored, so the
        // audit trail cannot be spoofed.
        if (!ActorResolver.TryResolve(http.User, out var actor))
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Caller has no stable identity.",
                type: "about:blank");

        await orchestrator.InjectFaultAsync(request, actor, ct);
        return Results.Accepted("/api/v1/chaos/faults", request with { TriggeredBy = actor.Id });
    })
    .RequireAuthorization(ControlPlane(ChaosPolicies.Operate))
    .RequireRateLimiting(HttpHardening.MutatePolicy)
    .AddEndpointFilter<CookieAntiforgeryFilter>();

// 4. MAP ROOT UI PATHS
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Stria.Client._Imports).Assembly);

app.Run();

// Control-plane routes accept the browser cookie or a bearer token, named explicitly so a
// change to the default scheme can never silently change who may call them.
static AuthorizeAttribute ControlPlane(string? policy) => new()
{
    AuthenticationSchemes = $"{CookieAuthenticationDefaults.AuthenticationScheme},{JwtBearerDefaults.AuthenticationScheme}",
    Policy = policy
};

static bool IsApi(HttpRequest request) => request.Path.StartsWithSegments("/api");

static Task SetStatus(HttpResponse response, int statusCode)
{
    response.StatusCode = statusCode;
    return Task.CompletedTask;
}

// Prevents open redirects: only same-site relative paths, never "//host" or "/\host", and no
// control characters (browsers strip tabs/newlines, which could turn "/\t/host" into "//host").
static string LocalUrl(string? url) =>
    url is ['/', ..]
    && (url.Length == 1 || url[1] is not ('/' or '\\'))
    && !url.Any(char.IsControl)
        ? url
        : "/";

public partial class Program { }
