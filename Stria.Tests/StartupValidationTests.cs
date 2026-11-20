using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Stria.Security;

namespace Stria.Tests;

/// <summary>
/// The startup rules called directly. StartupConfigurationTests checks the same rules through a
/// real host; these cover the cases it doesn't, without booting one.
/// </summary>
[TestClass]
public sealed class StartupValidationTests
{
    private const string ConnectionString = "Host=db;Database=stria;Username=stria;Password=unused";

    [TestMethod]
    public void MissingConnectionString_FailsFirst_NamingTheSetting()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Validate(Environments.Production));
        Assert.IsTrue(ex.Message.Contains("ConnectionStrings__Stria", StringComparison.Ordinal), ex.Message);
    }

    [TestMethod]
    public void Production_WithoutDataPlaneKeys_Fails()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Validate(Environments.Production,
            ("ConnectionStrings:Stria", ConnectionString),
            ("AzureAd:TenantId", "tenant"), ("AzureAd:ClientId", "client"), ("AzureAd:ClientSecret", "secret"),
            ("AzureAdApi:TenantId", "tenant"), ("AzureAdApi:ClientId", "client")));
        Assert.IsTrue(ex.Message.Contains("DataPlane:ServiceKeyHashes has no entries", StringComparison.Ordinal), ex.Message);
    }

    [TestMethod]
    public void Development_DoesNotRequireIdentityOrDataPlaneSettings() =>
        Validate(Environments.Development, ("ConnectionStrings:Stria", ConnectionString));

    private static void Validate(string environmentName, params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();
        StartupValidation.ThrowIfInvalid(configuration, new HostingEnvironment { EnvironmentName = environmentName });
    }
}
