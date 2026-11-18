using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Stria.Tests.TestSupport;

/// <summary>
/// Issues real, signed JWTs for API tests. The test host trusts only <see cref="Key"/> (see
/// <see cref="StriaApiFactory"/>), so token validation runs for real without contacting Entra.
/// </summary>
public static class TestTokens
{
    public const string Issuer = "https://stria-tests.local";
    public const string Audience = "api://stria-tests";
    public const string DefaultObjectId = "11111111-1111-1111-1111-111111111111";
    public const string DisplayName = "Test User";

    public static readonly RsaSecurityKey Key = new(RSA.Create(2048)) { KeyId = "stria-tests" };

    /// <param name="role">App role to include, or null for a signed-in caller with no role.</param>
    public static string Create(
        string? role,
        string oid = DefaultObjectId,
        string audience = Audience,
        DateTime? expires = null)
    {
        var exp = expires ?? DateTime.UtcNow.AddMinutes(10);

        // Every token carries "scp": Microsoft.Identity.Web rejects tokens with neither a scope
        // nor a role (IDW10201), and the no-role test must fail on authorization, not on that.
        var claims = new Dictionary<string, object>
        {
            ["oid"] = oid,
            ["name"] = DisplayName,
            ["scp"] = "Chaos.Access"
        };
        if (role is not null)
            claims["roles"] = new[] { role };

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Claims = claims,
            IssuedAt = exp.AddMinutes(-30),
            NotBefore = exp.AddMinutes(-30),
            Expires = exp,
            SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.RsaSha256)
        });
    }
}
