using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Stria.Tests;

/// <summary>No Docker needed: the host must refuse to start before it touches the database.</summary>
[TestClass]
public sealed class StartupConfigurationTests
{
    [TestMethod]
    public void Startup_OutsideDevelopment_WithoutIdentitySettings_Fails()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Stria", "Host=localhost;Database=unused;Username=unused;Password=unused");
            builder.UseSetting("AzureAd:TenantId", "");
            builder.UseSetting("AzureAd:ClientId", "");
            builder.UseSetting("AzureAd:ClientSecret", "");
            builder.UseSetting("AzureAdApi:TenantId", "");
            builder.UseSetting("AzureAdApi:ClientId", "");
        });

        var ex = Assert.Throws<Exception>(() => factory.CreateClient());

        var messages = new List<string>();
        for (Exception? e = ex; e is not null; e = e.InnerException)
            messages.Add(e.Message);
        Assert.IsTrue(
            messages.Any(m => m.Contains("Identity settings missing", StringComparison.Ordinal)),
            string.Join(" | ", messages));
        // The message names keys only and never echoes configuration values.
        Assert.IsTrue(messages.Any(m => m.Contains("AzureAd:ClientSecret", StringComparison.Ordinal)));
    }
}
