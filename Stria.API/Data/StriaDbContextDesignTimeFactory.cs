using Azure.Core;
using Azure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Stria.Security;
namespace Stria.Data;

/// <summary>
/// How the EF tools and the migrations bundle build a <see cref="StriaDbContext"/>. Without it
/// they would run Program.cs, which outside Development refuses to start without the Entra and
/// data-plane settings a migration never uses. It reads the app's own settings
/// (<c>ConnectionStrings__Stria</c>, <c>Database__UseEntraAuth</c>), so the migration job is
/// configured, and authenticates, exactly like the app.
/// </summary>
public sealed class StriaDbContextDesignTimeFactory : IDesignTimeDbContextFactory<StriaDbContext>
{
    public StriaDbContext CreateDbContext(string[] args)
    {
        // User secrets keep `dotnet ef database update` working locally; the bundle has none and
        // reads environment variables only.
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<StriaDbContextDesignTimeFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        return Create(configuration, new Lazy<TokenCredential>(() => new DefaultAzureCredential()));
    }

    /// <param name="credential">Created only when Entra auth is on.</param>
    internal static StriaDbContext Create(IConfiguration configuration, Lazy<TokenCredential> credential)
    {
        var connectionString = configuration.GetConnectionString("Stria")
                                ?? throw new InvalidOperationException(
                                    "Connection string 'Stria' is not configured. Set ConnectionStrings__Stria (never commit credentials)");

        var useEntraAuth = configuration.GetValue<bool>(StriaDataSource.UseEntraAuthKey);

        if (useEntraAuth && AzureRuntimeSettings.HasPassword(connectionString))
            throw new InvalidOperationException(
                $"{StriaDataSource.UseEntraAuthKey} is on, so ConnectionStrings:Stria must not contain a password.");

        var dataSource = StriaDataSource.Create(connectionString, useEntraAuth, credential);
        return new StriaDbContext(new DbContextOptionsBuilder<StriaDbContext>().UseNpgsql(dataSource).Options);
    }
}
