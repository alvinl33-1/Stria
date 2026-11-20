using System.Net;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
namespace Stria.Tests;

/// <summary>
/// No Docker needed. Behind ingress the OIDC redirect_uri must use the scheme the browser used,
/// which only the X-Forwarded-Proto header carries.
/// </summary>
[TestClass]
public sealed class ForwardedHeadersTests
{
    [TestMethod]
    public async Task EnabledForwardedHttps_BuildsHttpsRedirectUri()
    {
        var redirectUri = await SignInRedirectUriAsync(forwardedHeadersEnabled: true);
        StringAssert.StartsWith(redirectUri, "https://");
        StringAssert.EndsWith(redirectUri, "/signin-oidc");
    }

    [TestMethod]
    public async Task Disabled_ForwardedHttps_IsIgnored()
    {
        var redirectUri = await SignInRedirectUriAsync(forwardedHeadersEnabled: false);
        StringAssert.StartsWith(redirectUri, "http://");
    }
    private static async Task<string> SignInRedirectUriAsync(bool forwardedHeadersEnabled)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Stria", "Host=localhost;Database=unused;Username=unused;Password=unused");
            builder.UseSetting("Database:MigrateOnStartup", "false");
            builder.UseSetting("Expiry:SweepIntervalSeconds", "3600");
            builder.UseSetting("Retention:SweepIntervalMinutes", "60");
            builder.UseSetting("DataProtection:KeysPath", Path.Combine(Path.GetTempPath(), "stria-tests-dpkeys", Guid.NewGuid().ToString("N")));
            builder.UseSetting("AzureAd:TenantId", "00000000-0000-0000-0000-000000000001");
            builder.UseSetting("AzureAd:ClientId", "00000000-0000-0000-0000-000000000002");
            builder.UseSetting("AzureAdApi:TenantId", "00000000-0000-0000-0000-000000000001");
            builder.UseSetting("AzureAdApi:ClientId", "00000000-0000-0000-0000-000000000002");
            builder.UseSetting("ForwardedHeaders:Enabled", forwardedHeadersEnabled ? "true" : "false");
            // Never fetch Entra's discovery document; the challenge only needs an authorize URL.
            builder.ConfigureTestServices(services =>
                services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
                {
                    var config = new OpenIdConnectConfiguration
                    {
                        AuthorizationEndpoint = "https://login.example.test/authorize"
                    };
                    options.Configuration = config;
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(config);
                }));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/authentication/login");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-For", "203.0.113.10");
        var response = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.IsTrue(query.TryGetValue("redirect_uri", out var redirectUri), location.ToString());
        return redirectUri.ToString();
    }
}
