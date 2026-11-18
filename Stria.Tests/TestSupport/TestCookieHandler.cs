using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Stria.Tests.TestSupport;

/// <summary>
/// Test-only stand-in for the browser cookie scheme. <see cref="StriaApiFactory"/> swaps it in
/// for the "Cookies" scheme, so cookie-path behavior (antiforgery, policies) is testable without
/// an interactive Entra sign-in. Header format: "oid|role" (role may be empty).
/// This type exists only in the test assembly; the production host has no equivalent.
/// </summary>
public sealed class TestCookieHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Header = "X-Test-User";
    public const string DisplayName = "Cookie User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Header, out var value))
            return Task.FromResult(AuthenticateResult.NoResult());

        var parts = value.ToString().Split('|');
        var claims = new List<Claim> { new("oid", parts[0]), new("name", DisplayName) };
        if (parts.Length > 1 && parts[1].Length > 0)
            claims.Add(new Claim("roles", parts[1]));

        var identity = new ClaimsIdentity(claims, Scheme.Name, "name", "roles");
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
