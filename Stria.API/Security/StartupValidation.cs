using Stria.Data;
namespace Stria.Security;

/// <summary>
/// Every setting startup refuses to run without, checked before anything is registered. Outside
/// Development a missing identity value stops startup instead of running with broken sign-in;
/// there is deliberately no switch that turns authentication off. Messages name settings, never
/// their values.
/// </summary>
public static class StartupValidation
{
    private static readonly string[] RequiredIdentitySettings =
    [
        "AzureAd:TenantId", "AzureAd:ClientId", "AzureAd:ClientSecret",
        "AzureAdApi:TenantId", "AzureAdApi:ClientId"
    ];
    public static void ThrowIfInvalid(IConfiguration configuration, IHostEnvironment environment)
    {
        var connectionString = StriaDataSource.RequiredConnectionString(configuration);
        if (!environment.IsDevelopment())
        {
            var missing = RequiredIdentitySettings
                .Where(key => string.IsNullOrWhiteSpace(configuration[key]))
                .ToArray();
            if (missing.Length > 0)
                throw new InvalidOperationException(
                    $"Identity settings missing: {string.Join(", ", missing)}. Use user-secrets or environment variables.");
            if (!configuration.GetSection("DataPlane:ServiceKeyHashes").GetChildren().Any())
                throw new InvalidOperationException(
                    "DataPlane:ServiceKeyHashes has no entries, so no service could read its directives.");
        }
        // In every environment: a half-configured Azure deployment must not start.
        var missingAzure = AzureRuntimeSettings.Missing(configuration);
        if (missingAzure.Count > 0)
            throw new InvalidOperationException(
                $"Azure settings incomplete: {string.Join(", ", missingAzure)}. Set all of them, or none for local use.");
        if (configuration.GetValue<bool>(StriaDataSource.UseEntraAuthKey) && AzureRuntimeSettings.HasPassword(connectionString))
            throw new InvalidOperationException(
                $"{StriaDataSource.UseEntraAuthKey} is on, so ConnectionStrings:Stria must not contain a password.");
    }
}
