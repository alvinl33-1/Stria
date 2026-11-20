using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Identity.Web;
using Stria.Client.Security;
using Stria.Domain;
using Stria.DTOs;
using Stria.Security;
using Stria.Services;

namespace Stria.Endpoints;

/// <summary>
/// Operator routes. Each names its authentication schemes and role policy, a rate limit, and for
/// mutations the antiforgery filter, right where the route is mapped.
/// </summary>
public static class ControlPlaneEndpoints
{
    public static IEndpointRouteBuilder MapControlPlaneEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/me", GetMe)
            .RequireAuthorization(ControlPlane(policy: null))
            .RequireRateLimiting(HttpHardening.ReadPolicy);

        api.MapGet("/antiforgery/token", GetAntiforgeryToken)
            .RequireAuthorization(ControlPlane(ChaosPolicies.View))
            .RequireRateLimiting(HttpHardening.ReadPolicy);

        api.MapGet("/chaos/faults", GetActiveFaultsAsync)
            .RequireAuthorization(ControlPlane(ChaosPolicies.View))
            .RequireRateLimiting(HttpHardening.ReadPolicy);

        api.MapPost("/chaos/inject", InjectAsync)
            .RequireAuthorization(ControlPlane(ChaosPolicies.Operate))
            .RequireRateLimiting(HttpHardening.MutatePolicy)
            .AddEndpointFilter<CookieAntiforgeryFilter>();

        // Global kill switch: everyone who can view sees its state; only admins change it.
        api.MapGet("/chaos/kill-switch", GetKillSwitchAsync)
            .RequireAuthorization(ControlPlane(ChaosPolicies.View))
            .RequireRateLimiting(HttpHardening.ReadPolicy);

        api.MapPost("/chaos/kill-switch/engage", EngageKillSwitchAsync)
            .RequireAuthorization(ControlPlane(ChaosPolicies.Admin))
            .RequireRateLimiting(HttpHardening.MutatePolicy)
            .AddEndpointFilter<CookieAntiforgeryFilter>();

        api.MapPost("/chaos/kill-switch/release", ReleaseKillSwitchAsync)
            .RequireAuthorization(ControlPlane(ChaosPolicies.Admin))
            .RequireRateLimiting(HttpHardening.MutatePolicy)
            .AddEndpointFilter<CookieAntiforgeryFilter>();

        return api;
    }

    internal static Ok<CallerIdentity> GetMe(ClaimsPrincipal user) =>
        TypedResults.Ok(new CallerIdentity(user.GetObjectId(), user.FindFirstValue("name")));

    internal static Ok<AntiforgeryTokenResponse> GetAntiforgeryToken(IAntiforgery antiforgery, HttpContext http)
    {
        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(new AntiforgeryTokenResponse(antiforgery.GetAndStoreTokens(http).RequestToken));
    }

    internal static async Task<Ok<IReadOnlyList<ChaosExperiment>>> GetActiveFaultsAsync(
        IChaosOrchestrator orchestrator, CancellationToken ct) =>
        TypedResults.Ok(await orchestrator.GetAllActiveFaultsAsync(ct));

    internal static async Task<Results<Accepted<FaultInjectionRequest>, ProblemHttpResult>> InjectAsync(
        FaultInjectionRequest request,
        ClaimsPrincipal user,
        IChaosOrchestrator orchestrator,
        CancellationToken ct)
    {
        // Validation (targets, fault types, parameter bounds) lives in ChaosRules and is
        // re-enforced inside the orchestrator.
        if (!ChaosRules.TryValidate(request, out _, out var error))
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid fault injection request.",
                detail: error,
                type: "about:blank");

        // The actor comes from validated claims; the request's TriggeredBy is ignored, so the
        // audit trail cannot be spoofed.
        if (!ActorResolver.TryResolve(user, out var actor))
            return NoStableIdentity();

        try
        {
            await orchestrator.InjectFaultAsync(request, actor, ct);
        }
        catch (KillSwitchEngagedException)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Fault injection is suspended by the kill switch.",
                type: "about:blank");
        }

        // Relative to the /api/v1 group that Program.cs mounts this module under.
        return TypedResults.Accepted("/api/v1/chaos/faults", request with { TriggeredBy = actor.Id });
    }

    internal static async Task<Ok<KillSwitchState>> GetKillSwitchAsync(
        IChaosOrchestrator orchestrator, CancellationToken ct) =>
        TypedResults.Ok(await orchestrator.GetKillSwitchAsync(ct));

    internal static Task<Results<Ok<KillSwitchState>, ProblemHttpResult>> EngageKillSwitchAsync(
        KillSwitchRequest request, ClaimsPrincipal user, IChaosOrchestrator orchestrator, CancellationToken ct) =>
        ChangeKillSwitchAsync(request, user, (reason, actor) => orchestrator.EngageKillSwitchAsync(reason, actor, ct));

    internal static Task<Results<Ok<KillSwitchState>, ProblemHttpResult>> ReleaseKillSwitchAsync(
        KillSwitchRequest request, ClaimsPrincipal user, IChaosOrchestrator orchestrator, CancellationToken ct) =>
        ChangeKillSwitchAsync(request, user, (reason, actor) => orchestrator.ReleaseKillSwitchAsync(reason, actor, ct));

    private static async Task<Results<Ok<KillSwitchState>, ProblemHttpResult>> ChangeKillSwitchAsync(
        KillSwitchRequest request, ClaimsPrincipal user, Func<string, Actor, Task<KillSwitchState>> change)
    {
        if (!KillSwitchEvent.TryNormalizeReason(request.Reason, out var reason, out var error))
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid kill switch request.",
                detail: error,
                type: "about:blank");

        if (!ActorResolver.TryResolve(user, out var actor))
            return NoStableIdentity();

        return TypedResults.Ok(await change(reason, actor));
    }

    // Control-plane routes accept the browser cookie or a bearer token, named explicitly so a
    // change to the default scheme can never silently change who may call them.
    private static AuthorizeAttribute ControlPlane(string? policy) => new()
    {
        AuthenticationSchemes = $"{CookieAuthenticationDefaults.AuthenticationScheme},{JwtBearerDefaults.AuthenticationScheme}",
        Policy = policy
    };

    private static ProblemHttpResult NoStableIdentity() => TypedResults.Problem(
        statusCode: StatusCodes.Status403Forbidden,
        title: "Caller has no stable identity.",
        type: "about:blank");
}

/// <summary>Serialized as <c>{"oid":…,"name":…}</c>, as before.</summary>
internal sealed record CallerIdentity(string? Oid, string? Name);

/// <summary>Serialized as <c>{"token":…}</c>, as before.</summary>
internal sealed record AntiforgeryTokenResponse(string? Token);
