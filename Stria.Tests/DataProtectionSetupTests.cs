using Azure.Core;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Stria.Security;

namespace Stria.Tests;

/// <summary>
/// No Docker or Azure needed: these check which key store is configured, not that Azure accepts
/// it. The credential throws if anything asks it for a token.
/// </summary>
[TestClass]
public sealed class DataProtectionSetupTests
{
    [TestMethod]
    public void NoBlobUri_KeepsFileSystemKeys()
    {
        var options = KeyManagement(new Dictionary<string, string?>
        {
            ["DataProtection:KeysPath"] = Path.Combine(Path.GetTempPath(), "stria-tests-dpkeys", Guid.NewGuid().ToString("N"))
        }, out var credentialCreated);

        Assert.IsInstanceOfType<FileSystemXmlRepository>(options.XmlRepository);
        Assert.IsNull(options.XmlEncryptor);
        Assert.IsFalse(credentialCreated(), "The Compose path must not build an Azure credential.");
    }

    [TestMethod]
    public void BlobUriAndKeyVaultKey_UseBlobRepositoryAndKeyVaultEncryptor()
    {
        var options = KeyManagement(new Dictionary<string, string?>
        {
            [DataProtectionSetup.BlobUriKey] = "https://striatest.blob.core.windows.net/dpkeys/keys.xml",
            [DataProtectionSetup.KeyVaultKeyUriKey] = "https://stria-test.vault.azure.net/keys/dataprotection"
        }, out var credentialCreated);

        StringAssert.Contains(options.XmlRepository!.GetType().Name, "AzureBlob");
        StringAssert.Contains(options.XmlEncryptor!.GetType().Name, "KeyVault");
        Assert.IsTrue(credentialCreated());
    }

    [TestMethod]
    public void BlobUriWithoutKeyVaultKey_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => KeyManagement(new Dictionary<string, string?>
        {
            [DataProtectionSetup.BlobUriKey] = "https://striatest.blob.core.windows.net/dpkeys/keys.xml"
        }, out _));

        StringAssert.Contains(ex.Message, DataProtectionSetup.KeyVaultKeyUriKey);
    }

    private static KeyManagementOptions KeyManagement(
        Dictionary<string, string?> settings, out Func<bool> credentialCreated)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var credential = new Lazy<TokenCredential>(() => new NoTokenCredential());
        credentialCreated = () => credential.IsValueCreated;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStriaDataProtection(configuration, credential);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;
    }

    private sealed class NoTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new AssertFailedException("A configuration test must not request an Azure token.");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new AssertFailedException("A configuration test must not request an Azure token.");
    }
}
