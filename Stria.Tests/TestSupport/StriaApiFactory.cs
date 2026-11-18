using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Stria.Tests.TestSupport;

/// <summary>
/// Boots the real Stria host against a Testcontainers PostgreSQL database.
/// Interview talking point: WebApplicationFactory runs Program.cs in-process so you exercise
/// middleware + routing + DI without publishing a container for every test.
/// </summary>
public sealed class StriaApiFactory : WebApplicationFactory<Program>
{
    private readonly PostgresFixture _postgres;
    private readonly IReadOnlyDictionary<string, string?> _overrides;
    private readonly string _dpKeysPath;

    /// <param name="overrides">Configuration applied after the test defaults (e.g. tighter rate limits).</param>
    public StriaApiFactory(PostgresFixture postgres, IReadOnlyDictionary<string, string?>? overrides = null)
    {
        _postgres = postgres;
        _overrides = overrides ?? new Dictionary<string, string?>();
        _dpKeysPath = Path.Combine(Path.GetTempPath(), "stria-tests-dpkeys", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dpKeysPath);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // UseSetting values flow into IConfiguration before Program.cs reads ConnectionStrings:Stria.
        builder.UseSetting("ConnectionStrings:Stria", _postgres.ConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "false"); // schema already applied by PostgresFixture
        builder.UseSetting("DataProtection:KeysPath", _dpKeysPath);
        builder.UseSetting("Expiry:SweepIntervalSeconds", "3600");
        builder.UseSetting("Retention:SweepIntervalMinutes", "60");

        // Placeholder identity settings: Microsoft.Identity.Web validates that they exist, but
        // the overrides below mean nothing ever contacts Entra.
        builder.UseSetting("AzureAd:TenantId", "00000000-0000-0000-0000-000000000001");
        builder.UseSetting("AzureAd:ClientId", "00000000-0000-0000-0000-000000000002");
        builder.UseSetting("AzureAdApi:TenantId", "00000000-0000-0000-0000-000000000001");
        builder.UseSetting("AzureAdApi:ClientId", "00000000-0000-0000-0000-000000000002");

        // High limits so ordinary tests never hit 429; the rate-limit tests override these.
        builder.UseSetting("RateLimits:ReadPermitLimit", "10000");
        builder.UseSetting("RateLimits:MutatePermitLimit", "10000");

        foreach (var (key, value) in _overrides)
            builder.UseSetting(key, value);

        builder.ConfigureTestServices(services =>
        {
            // Bearer: trust only the test signing key and never fetch Entra metadata.
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                    new OpenIdConnectConfiguration { Issuer = TestTokens.Issuer });

                var p = options.TokenValidationParameters;
                p.ValidIssuer = TestTokens.Issuer;
                p.ValidAudiences = [TestTokens.Audience];
                p.IssuerSigningKey = TestTokens.Key;
                // Remove Entra-specific validators that expect real Entra issuers and keys.
                p.IssuerValidator = null;
                p.AudienceValidator = null;
                p.IssuerSigningKeyValidator = null;
                p.IssuerSigningKeyValidatorUsingConfiguration = null;
            });

            // Cookie: swap the real cookie handler for the header-driven test handler.
            services.Configure<AuthenticationOptions>(options =>
                options.SchemeMap[CookieAuthenticationDefaults.AuthenticationScheme].HandlerType =
                    typeof(TestCookieHandler));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            try
            {
                if (Directory.Exists(_dpKeysPath))
                    Directory.Delete(_dpKeysPath, recursive: true);
            }
            catch
            {
                // Best-effort temp cleanup; test hosts may still hold a file handle briefly.
            }
        }
    }
}
