using Npgsql;
using Stria.Data;

namespace Stria.Security;

/// <summary>
/// The Azure settings only work as a set. Passwordless PostgreSQL with keys still on local disk,
/// for example, starts fine and then breaks every cookie on the next restart. If any one of them
/// is configured, startup requires all of them.
/// </summary>
public static class AzureRuntimeSettings
{
    public const string ForwardedHeadersKey = "ForwardedHeaders:Enabled";

    /// <summary>Names (never values) of the settings a half-configured Azure deployment lacks.</summary>
    public static IReadOnlyList<string> Missing(IConfiguration configuration)
    {
        var blobUri = Has(configuration, DataProtectionSetup.BlobUriKey);
        var keyVaultKey = Has(configuration, DataProtectionSetup.KeyVaultKeyUriKey);
        var entraAuth = configuration.GetValue<bool>(StriaDataSource.UseEntraAuthKey);

        // Forwarded headers alone are not Azure-specific: any reverse proxy may need them.
        if (!blobUri && !keyVaultKey && !entraAuth)
            return [];

        var missing = new List<string>();
        if (!blobUri) missing.Add(DataProtectionSetup.BlobUriKey);
        if (!keyVaultKey) missing.Add(DataProtectionSetup.KeyVaultKeyUriKey);
        if (!entraAuth) missing.Add($"{StriaDataSource.UseEntraAuthKey}=true");
        if (!configuration.GetValue<bool>(ForwardedHeadersKey)) missing.Add($"{ForwardedHeadersKey}=true");
        return missing;
    }

    /// <summary>With Entra auth the token is the password; a stored one is a leftover secret.</summary>
    public static bool HasPassword(string connectionString) =>
        !string.IsNullOrEmpty(new NpgsqlConnectionStringBuilder(connectionString).Password);

    private static bool Has(IConfiguration configuration, string key) =>
        !string.IsNullOrWhiteSpace(configuration[key]);
}
