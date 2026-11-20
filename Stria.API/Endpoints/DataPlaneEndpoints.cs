using Microsoft.AspNetCore.Http.HttpResults;
using Stria.Domain;
using Stria.DTOs;
using Stria.Security;
using Stria.Services;

namespace Stria.Endpoints;

/// <summary>
/// The feed sample services poll. Authenticated by the calling service's own data-plane key,
/// never an operator credential.
/// </summary>
public static class DataPlaneEndpoints
{
    public static IEndpointRouteBuilder MapDataPlaneEndpoints(this IEndpointRouteBuilder api)
    {
        // What should service {service} be doing right now?
        //   200 + FaultDirective  -> apply it for RemainingSeconds
        //   204                   -> no active fault
        //   404                   -> not a registered service
        // No key -> 401; another service's key -> 403 (OwnDirectiveHandler reads {service}).
        api.MapGet("/chaos/directives/{service}", GetDirectiveAsync)
            .RequireAuthorization(DataPlaneDefaults.Policy)
            .RequireRateLimiting(HttpHardening.DataPlanePolicy);
        return api;
    }

    internal static async Task<Results<Ok<FaultDirective>, NoContent, ProblemHttpResult>> GetDirectiveAsync(
        string service,
        IChaosOrchestrator orchestrator,
        TimeProvider time,
        CancellationToken ct)
    {
        if (!ChaosRules.TryGetService(service, out var canonical))
            return TypedResults.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Unknown target service.",
                type: "about:blank");

        var experiment = await orchestrator.GetActiveFaultForServiceAsync(canonical, ct);
        if (experiment is null)
            return TypedResults.NoContent();

        // Hand out a duration, not a timestamp: the consumer counts it down on its own monotonic
        // clock, so clock skew between machines can't stretch or shorten the fault.
        var remaining = (int)Math.Ceiling((experiment.ExpiresAt - time.GetUtcNow().UtcDateTime).TotalSeconds);
        if (remaining < 1)
            return TypedResults.NoContent(); // expired between the query and now

        return TypedResults.Ok(new FaultDirective(
            experiment.Id,
            experiment.FaultType,
            experiment.LatencyMs,
            experiment.Percent,
            experiment.StatusCode,
            remaining));
    }
}
