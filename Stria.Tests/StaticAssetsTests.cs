using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Stria.Tests;

/// <summary>
/// No Docker needed. Every stylesheet and script a page references must be served, anonymously:
/// a missing blazor.web.js leaves the dashboard stuck on its prerendered "Loading..." state, and a
/// missing scoped-CSS bundle shows Blazor's error bar by default.
/// </summary>
[TestClass]
public sealed partial class StaticAssetsTests
{
    [GeneratedRegex("""<(?:link[^>]*rel="stylesheet"[^>]*href|script[^>]*src)="(?<url>[^"]+)""")]
    private static partial Regex AssetReference();

    [TestMethod]
    public async Task AnonymousPage_AllReferencedAssets_AreServed()
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
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var page = await client.GetAsync("/access-denied");
        Assert.AreEqual(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();

        var assets = AssetReference().Matches(html).Select(m => m.Groups["url"].Value).ToList();
        Assert.IsTrue(assets.Any(a => a.Contains("blazor.web", StringComparison.Ordinal)), html);
        Assert.IsTrue(assets.Any(a => a.Contains(".styles.css", StringComparison.Ordinal)), html);

        foreach (var asset in assets)
        {
            var response = await client.GetAsync("/" + asset.TrimStart('/'));
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, $"{asset} was not served.");
        }
    }
}
