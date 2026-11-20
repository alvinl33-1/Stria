using Azure.Core;
using Microsoft.AspNetCore.DataProtection;

namespace Stria.Security;

/// <summary>
/// Where the Data Protection key ring lives. Cookies and antiforgery tokens are encrypted with it,
/// so every replica must read the same keys and they must survive a restart.
/// Compose: a Docker volume. Azure: a Blob container, with each key wrapped by a Key Vault key.
/// </summary>
public static class DataProtectionSetup
{
    public const string BlobUriKey = "DataProtection:BlobUri";
    public const string KeyVaultKeyUriKey = "DataProtection:KeyVaultKeyUri";

    /// <param name="credential">Created only when the Azure path is configured.</param>
    public static IDataProtectionBuilder AddStriaDataProtection(
        this IServiceCollection services, IConfiguration configuration, Lazy<TokenCredential> credential)
    {
        var dataProtection = services.AddDataProtection().SetApplicationName("Stria.API");

        var blobUri = configuration[BlobUriKey];
        if (string.IsNullOrWhiteSpace(blobUri))
            return dataProtection.PersistKeysToFileSystem(new DirectoryInfo(
                configuration["DataProtection:KeysPath"] ?? "/var/lib/stria/dpkeys"));

        // Keys stored in Blob without a wrapping key would sit there in plain text.
        var keyUri = configuration[KeyVaultKeyUriKey];
        if (string.IsNullOrWhiteSpace(keyUri))
            throw new InvalidOperationException($"{BlobUriKey} is set but {KeyVaultKeyUriKey} is not.");

        return dataProtection
            .PersistKeysToAzureBlobStorage(new Uri(blobUri), credential.Value)
            .ProtectKeysWithAzureKeyVault(new Uri(keyUri), credential.Value);
    }
}
