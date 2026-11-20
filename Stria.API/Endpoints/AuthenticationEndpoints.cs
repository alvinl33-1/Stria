using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Stria.Endpoints;

/// <summary>
/// Browser sign-in and sign-out. Sign-out is a POST so a cross-site link cannot log an operator
/// out; its form-field binding makes UseAntiforgery() validate the token automatically.
/// </summary>
public static class AuthenticationEndpoints
{
    public static IEndpointRouteBuilder MapAuthenticationEndpoints(this IEndpointRouteBuilder app)
    {
        var authentication = app.MapGroup("/authentication");
        authentication.MapGet("/login", Login).AllowAnonymous();
        authentication.MapPost("/logout", Logout);
        return app;
    }

    internal static ChallengeHttpResult Login(string? returnUrl) => TypedResults.Challenge(
        new AuthenticationProperties { RedirectUri = LocalUrl(returnUrl) },
        [OpenIdConnectDefaults.AuthenticationScheme]);

    internal static SignOutHttpResult Logout([FromForm] string? returnUrl) => TypedResults.SignOut(
        new AuthenticationProperties { RedirectUri = LocalUrl(returnUrl) },
        [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]);

    /// <summary>
    /// Prevents open redirects: only same-site relative paths, never "//host" or "/\host", and no
    /// control characters (browsers strip tabs/newlines, which could turn "/\t/host" into "//host").
    /// </summary>
    internal static string LocalUrl(string? url) =>
        url is ['/', ..]
        && (url.Length == 1 || url[1] is not ('/' or '\\'))
        && !url.Any(char.IsControl)
            ? url
            : "/";
}
