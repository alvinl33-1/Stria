using Azure.Core;
using Npgsql;

namespace Stria.Data;

/// <summary>
/// Builds the one <see cref="NpgsqlDataSource"/> the app uses. Compose and tests authenticate
/// with the password in the connection string. Azure (<c>Database:UseEntraAuth=true</c>) has no
/// password: an Entra token for the managed identity is sent instead, refreshed before it expires.
/// </summary>
public static class StriaDataSource
{
    public const string UseEntraAuthKey = "Database:UseEntraAuth";

    /// <summary>The token audience Azure Database for PostgreSQL accepts.</summary>
    public const string PostgresTokenScope = "https://ossrdbms-aad.database.windows.net/.default";

    public const string ConnectionStringName = "Stria";

    /// <summary>
    /// The app's connection string, or a startup error that names the setting (never its value).
    /// Startup validation and persistence both read it here, so the message lives in one place.
    /// </summary>
    public static string RequiredConnectionString(IConfiguration configuration) =>
        configuration.GetConnectionString(ConnectionStringName)
        ?? throw new InvalidOperationException(
            "Connection string 'Stria' is not configured. Set ConnectionStrings__Stria (never commit credentials).");

    /// <param name="credential">Created only when Entra auth is on.</param>
    public static NpgsqlDataSource Create(string connectionString, bool useEntraAuth, Lazy<TokenCredential> credential)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        if (useEntraAuth)
        {
            var request = new TokenRequestContext([PostgresTokenScope]);
            // Entra tokens last about an hour; refresh well before that, and retry quickly after a failure.
            builder.UsePeriodicPasswordProvider(
                async (_, ct) => (await credential.Value.GetTokenAsync(request, ct)).Token,
                successRefreshInterval: TimeSpan.FromMinutes(55),
                failureRefreshInterval: TimeSpan.FromSeconds(10));
        }

        return builder.Build();
    }
}
