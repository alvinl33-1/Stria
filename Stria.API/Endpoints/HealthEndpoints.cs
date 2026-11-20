using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Stria.Data;

namespace Stria.Endpoints;

/// <summary>
/// Anonymous probes. /healthz: the process is up. /readyz: it can also reach PostgreSQL, so
/// compose-smoke.sh (and later the Container Apps probes) only send traffic once it is useful.
/// </summary>
public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        // A one-line handler stays a lambda; anything with logic gets a named method.
        app.MapGet("/healthz", () => TypedResults.Ok(new ProbeStatus("ok"))).AllowAnonymous();
        app.MapGet("/readyz", ReadyAsync).AllowAnonymous();
        return app;
    }

    internal static async Task<Results<Ok<ProbeStatus>, StatusCodeHttpResult>> ReadyAsync(
        IDbContextFactory<StriaDbContext> dbFactory,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            if (await db.Database.CanConnectAsync(ct))
                return TypedResults.Ok(new ProbeStatus("ready"));
        }
        catch (NpgsqlException ex)
        {
            logger.LogWarning(ex, "Readiness check could not connect to PostgreSQL.");
        }

        return TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
}

/// <summary>Serialized as <c>{"status":"…"}</c>, the same body the anonymous objects produced.</summary>
internal sealed record ProbeStatus(string Status);
