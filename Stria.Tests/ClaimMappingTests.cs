using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Stria.Client.Security;

namespace Stria.Tests;

/// <summary>
/// No Docker needed. Validates a synthetic Entra-shaped token with the token handler each real
/// scheme ends up with after all options configuration (including Microsoft.Identity.Web's),
/// and checks that the short "roles" claim satisfies role checks. StriaApiFactory replaces the
/// browser sign-in with a test cookie, so these tests are what cover the real OIDC handler.
/// </summary>
[TestClass]
public sealed class ClaimMappingTests
{
    private const string ObjectId = "11111111-2222-3333-4444-555555555555";

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Stria", "Host=localhost;Database=unused;Username=unused;Password=unused");
            builder.UseSetting("Database:MigrateOnStartup", "false");
            builder.UseSetting("DataProtection:KeysPath", Path.Combine(Path.GetTempPath(), "stria-tests-dpkeys", Guid.NewGuid().ToString("N")));
            builder.UseSetting("AzureAd:TenantId", "00000000-0000-0000-0000-000000000001");
            builder.UseSetting("AzureAd:ClientId", "00000000-0000-0000-0000-000000000002");
            builder.UseSetting("AzureAd:ClientSecret", "unused");
            builder.UseSetting("AzureAdApi:TenantId", "00000000-0000-0000-0000-000000000001");
            builder.UseSetting("AzureAdApi:ClientId", "00000000-0000-0000-0000-000000000002");
        });

    [TestMethod]
    public async Task BrowserSignIn_EntraRolesClaim_GrantsRole()
    {
        using var factory = CreateFactory();
        var options = factory.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);

        var identity = await ValidateAsync(options.TokenHandler, options.TokenValidationParameters);

        AssertShortEntraClaims(identity);
    }

    [TestMethod]
    public async Task BearerToken_EntraRolesClaim_GrantsRole()
    {
        using var factory = CreateFactory();
        var options = factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        var identity = await ValidateAsync(options.TokenHandlers.First(), options.TokenValidationParameters);

        AssertShortEntraClaims(identity);
    }

    private static void AssertShortEntraClaims(ClaimsIdentity identity)
    {
        var user = new ClaimsPrincipal(identity);
        Assert.IsTrue(user.IsInRole(ChaosRoles.Admin), DescribeClaims(identity));
        Assert.AreEqual(ObjectId, user.FindFirstValue("oid"), DescribeClaims(identity));
        Assert.AreEqual("Test Admin", identity.Name, DescribeClaims(identity));
    }

    private static async Task<ClaimsIdentity> ValidateAsync(TokenHandler handler, TokenValidationParameters configured)
    {
        var key = new SymmetricSecurityKey(new byte[32]);
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Claims = new Dictionary<string, object>
            {
                ["oid"] = ObjectId,
                ["name"] = "Test Admin",
                ["roles"] = new[] { ChaosRoles.Admin }
            },
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        });

        // Keep the configured claim types; only replace what needs real Entra metadata.
        var parameters = configured.Clone();
        parameters.ConfigurationManager = null;
        parameters.IssuerSigningKey = key;
        parameters.ValidateIssuer = false;
        parameters.IssuerValidator = null;
        parameters.ValidateAudience = false;

        var result = await handler.ValidateTokenAsync(token, parameters);
        Assert.IsTrue(result.IsValid, result.Exception?.ToString());
        return result.ClaimsIdentity;
    }

    private static string DescribeClaims(ClaimsIdentity identity) =>
        $"RoleClaimType={identity.RoleClaimType}; claims: " +
        string.Join(", ", identity.Claims.Select(c => c.Type));
}
