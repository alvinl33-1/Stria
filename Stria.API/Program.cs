// Stria.API/Program.cs

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Stria;
using Stria.Components;
using Stria.Data;
using Stria.Domain;
using Stria.DTOs;
using Stria.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();

// Services
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

// Use the browser's current origin for server-rendered interactive components. This keeps
// API calls valid behind HTTPS ingress and avoids environment-specific hostnames.
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(sp.GetRequiredService<NavigationManager>().BaseUri)
});

// Persistence. A DbContext *factory* (not a scoped DbContext) lets the singleton orchestrator
// and the hosted services create a short-lived context per operation.
var connectionString = builder.Configuration.GetConnectionString("Stria")
    ?? throw new InvalidOperationException(
        "Connection string 'Stria' is not configured. Set ConnectionStrings__Stria (never commit credentials).");
builder.Services.AddDbContextFactory<StriaDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<ExperimentRetentionOptions>(
    builder.Configuration.GetSection(ExperimentRetentionOptions.SectionName));
builder.Services.Configure<ExperimentExpiryOptions>(
    builder.Configuration.GetSection(ExperimentExpiryOptions.SectionName));

builder.Services.AddSingleton<IChaosOrchestrator, DatabaseChaosOrchestrator>();
builder.Services.AddHostedService<TrafficSimulatorService>();
builder.Services.AddHostedService<ExperimentRetentionService>();
builder.Services.AddHostedService<ExperimentExpiryService>();

builder.Services.AddDataProtection()
    .SetApplicationName("Stria.API")
    .PersistKeysToFileSystem(new DirectoryInfo(
        builder.Configuration["DataProtection:KeysPath"] ?? "/var/lib/stria/dpkeys"));

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
app.UseAntiforgery();
app.UseStatusCodePages();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
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
});

var api = app.MapGroup("/api/v1");

api.MapGet("/chaos/faults", async (IChaosOrchestrator orchestrator, CancellationToken ct) =>
    Results.Ok(await orchestrator.GetAllActiveFaultsAsync(ct)));

// Data-plane feed: what should service {service} be doing right now?
//   200 + FaultDirective  -> apply it for RemainingSeconds
//   204                   -> no active fault
//   404                   -> not a registered service
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
});

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

    // TriggeredBy is derived on the server and the client-supplied value is ignored, so the
    // audit trail cannot be spoofed. "anonymous" until authentication is added; once it is,
    // prefer a stable claim (e.g. sub / NameIdentifier) over a display name.
    var actor = http.User.Identity?.Name ?? "anonymous";

    await orchestrator.InjectFaultAsync(request, actor, ct);
    return Results.Accepted("/api/v1/chaos/faults", request with { TriggeredBy = actor });
});

// 4. MAP ROOT UI PATHS
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Stria.Client._Imports).Assembly);

app.Run();

public partial class Program { }
