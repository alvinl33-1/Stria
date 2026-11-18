using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Stria.Security;

/// <summary>
/// Minimal-API JSON endpoints are not covered by <c>UseAntiforgery()</c>, so cookie-authenticated
/// mutations validate the token here. Bearer callers are exempt: a browser never attaches a bearer
/// token on its own, so they cannot be the target of cross-site request forgery.
/// </summary>
internal sealed class CookieAntiforgeryFilter(IAntiforgery antiforgery, ILogger<CookieAntiforgeryFilter> logger)
    : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        // Exempt only when a bearer token actually authenticated. Checking for the mere presence of
        // an Authorization header would let a cookie request skip validation with "Bearer junk".
        var bearer = await http.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
        if (bearer.Succeeded)
            return await next(context);

        try
        {
            await antiforgery.ValidateRequestAsync(http);
        }
        catch (AntiforgeryValidationException)
        {
            logger.LogWarning("Antiforgery validation failed for {Method} {Path}.", http.Request.Method, http.Request.Path);
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Missing or invalid antiforgery token.",
                type: "about:blank");
        }

        return await next(context);
    }
}
