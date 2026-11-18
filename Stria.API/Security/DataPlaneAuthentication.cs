using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Stria.Domain;

namespace Stria.Security;

public static class DataPlaneDefaults
{
    public const string AuthenticationScheme = "DataPlane";
    public const string Policy = "DataPlane.OwnDirective";
    public const string HeaderName = "X-Stria-Service-Key";
    public const string ServiceClaim = "stria:service";
}

/// <summary>"DataPlane" section: service name -> lowercase hex SHA-256 of that service's key.</summary>
public sealed class DataPlaneOptions
{
    public const string SectionName = "DataPlane";

    public Dictionary<string, string> ServiceKeyHashes { get; set; } = new();
}

public sealed class DataPlaneKeys
{
    public static byte[] Hash(string key) => SHA256.HashData(Encoding.UTF8.GetBytes(key));

    public static string HashHex(string key) => Convert.ToHexStringLower(Hash(key));

    public static bool IsValid(DataPlaneOptions options) => options.ServiceKeyHashes.All(entry =>
        ChaosRules.TryGetService(entry.Key, out _)
        && entry.Value.Length == 64
        && entry.Value.All(char.IsAsciiHexDigit));
}

/// <summary>Authenticates a polling service by its key. Never logs the key itself.</summary>
public sealed class DataPlaneAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptionsMonitor<DataPlaneOptions> dataPlane)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(DataPlaneDefaults.HeaderName, out var values))
            return Task.FromResult(AuthenticateResult.NoResult());

        if (values.Count != 1 || string.IsNullOrWhiteSpace(values[0]))
            return Task.FromResult(AuthenticateResult.Fail("Malformed data-plane key header."));

        var presented = DataPlaneKeys.Hash(values[0]!);
        string? service = null;

        // Check every entry, so response time doesn't reveal which one matched.
        foreach (var (name, hashHex) in dataPlane.CurrentValue.ServiceKeyHashes)
        {
            if (CryptographicOperations.FixedTimeEquals(presented, Convert.FromHexString(hashHex))
                && ChaosRules.TryGetService(name, out var canonical))
                service = canonical;
        }

        if (service is null)
        {
            Logger.LogWarning("Rejected a data-plane request with an unrecognised key.");
            return Task.FromResult(AuthenticateResult.Fail("Invalid data-plane key."));
        }

        var identity = new ClaimsIdentity(
            [new Claim(DataPlaneDefaults.ServiceClaim, service)], DataPlaneDefaults.AuthenticationScheme);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), DataPlaneDefaults.AuthenticationScheme)));
    }
}

/// <summary>A service may read only /directives/{its own name}.</summary>
public sealed class OwnDirectiveRequirement : IAuthorizationRequirement { }

public sealed class OwnDirectiveHandler : AuthorizationHandler<OwnDirectiveRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, OwnDirectiveRequirement requirement)
    {
        if (context.Resource is HttpContext http
            && ChaosRules.TryGetService(http.GetRouteValue("service") as string, out var requested)
            && context.User.FindFirstValue(DataPlaneDefaults.ServiceClaim) == requested)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
