using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Stria.Data;
using Stria.Security;

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

    private const string BlobUri = "https://striatest.blob.core.windows.net/dpkeys/keys.xml";
    private const string KeyUri = "https://stria-test.vault.azure.net/keys/dataprotection";

    [TestMethod]
    public void AzureSettings_NoneSet_NothingMissing()
    {
        Assert.IsEmpty(AzureRuntimeSettings.Missing(Config()));
        // Forwarded headers alone are not Azure mode.
        Assert.IsEmpty(AzureRuntimeSettings.Missing(Config((AzureRuntimeSettings.ForwardedHeadersKey, "true"))));
    }

    [TestMethod]
    public void AzureSettings_AllSet_NothingMissing() =>
        Assert.IsEmpty(AzureRuntimeSettings.Missing(Config(
            (DataProtectionSetup.BlobUriKey, BlobUri),
            (DataProtectionSetup.KeyVaultKeyUriKey, KeyUri),
            (StriaDataSource.UseEntraAuthKey, "true"),
            (AzureRuntimeSettings.ForwardedHeadersKey, "true"))));

    [TestMethod]
    [DataRow("DataProtection:BlobUri")]
    [DataRow("DataProtection:KeyVaultKeyUri")]
    [DataRow("Database:UseEntraAuth")]
    public void AzureSettings_OnlyOneSet_NamesTheRest(string onlySet)
    {
        var value = onlySet switch
        {
            DataProtectionSetup.BlobUriKey => BlobUri,
            DataProtectionSetup.KeyVaultKeyUriKey => KeyUri,
            _ => "true"
        };

        var missing = AzureRuntimeSettings.Missing(Config((onlySet, value)));

        Assert.HasCount(3, missing);
        Assert.IsFalse(missing.Any(m => m.StartsWith(onlySet, StringComparison.Ordinal)));
        CollectionAssert.Contains(missing.ToList(), $"{AzureRuntimeSettings.ForwardedHeadersKey}=true");
    }

    [TestMethod]
    public void AzureSettings_EntraAuthOff_IsReported() =>
        CollectionAssert.AreEqual(
            new[] { $"{StriaDataSource.UseEntraAuthKey}=true" },
            AzureRuntimeSettings.Missing(Config(
                (DataProtectionSetup.BlobUriKey, BlobUri),
                (DataProtectionSetup.KeyVaultKeyUriKey, KeyUri),
                (StriaDataSource.UseEntraAuthKey, "false"),
                (AzureRuntimeSettings.ForwardedHeadersKey, "true"))).ToArray());

    [TestMethod]
    public void HasPassword_DetectsStoredPassword()
    {
        Assert.IsTrue(AzureRuntimeSettings.HasPassword("Host=db;Database=stria;Username=stria;Password=x"));
        Assert.IsFalse(AzureRuntimeSettings.HasPassword("Host=db;Database=stria;Username=stria-identity;SslMode=Require"));
    }

    [TestMethod]
    public void Startup_HalfConfiguredAzure_Fails()
    {
        using var factory = DevelopmentHost(
            ("ConnectionStrings:Stria", "Host=localhost;Database=unused;Username=unused;Password=unused"),
            (DataProtectionSetup.BlobUriKey, BlobUri));

        var messages = StartupFailure(factory);

        Assert.IsTrue(messages.Any(m => m.Contains("Azure settings incomplete", StringComparison.Ordinal)), string.Join(" | ", messages));
        Assert.IsFalse(messages.Any(m => m.Contains(BlobUri, StringComparison.Ordinal)), "Values must not be echoed.");
    }

    [TestMethod]
    public void Startup_EntraAuthWithStoredPassword_Fails()
    {
        using var factory = DevelopmentHost(
            ("ConnectionStrings:Stria", "Host=localhost;Database=unused;Username=unused;Password=leftover-secret"),
            (DataProtectionSetup.BlobUriKey, BlobUri),
            (DataProtectionSetup.KeyVaultKeyUriKey, KeyUri),
            (StriaDataSource.UseEntraAuthKey, "true"),
            (AzureRuntimeSettings.ForwardedHeadersKey, "true"));

        var messages = StartupFailure(factory);

        Assert.IsTrue(messages.Any(m => m.Contains("must not contain a password", StringComparison.Ordinal)), string.Join(" | ", messages));
        Assert.IsFalse(messages.Any(m => m.Contains("leftover-secret", StringComparison.Ordinal)), "Values must not be echoed.");
    }

    private static IConfiguration Config(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    private static WebApplicationFactory<Program> DevelopmentHost(params (string Key, string Value)[] settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            foreach (var (key, value) in settings)
                builder.UseSetting(key, value);
        });

    private static List<string> StartupFailure(WebApplicationFactory<Program> factory)
    {
        var ex = Assert.Throws<Exception>(() => factory.CreateClient());
        var messages = new List<string>();
        for (Exception? e = ex; e is not null; e = e.InnerException)
            messages.Add(e.Message);
        return messages;
    }
}
