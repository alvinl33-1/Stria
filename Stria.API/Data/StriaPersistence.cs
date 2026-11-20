using Azure.Core;
using Microsoft.EntityFrameworkCore;

namespace Stria.Data;

/// <summary>
/// One <see cref="Npgsql.NpgsqlDataSource"/> for the whole app, and a DbContext *factory* (not a
/// scoped DbContext) so the singleton orchestrator and the hosted services can create a
/// short-lived context per operation.
/// </summary>
public static class StriaPersistence
{
    /// <param name="credential">Used only when Database:UseEntraAuth is on.</param>
    public static IServiceCollection AddStriaPersistence(
        this IServiceCollection services, IConfiguration configuration, Lazy<TokenCredential> credential)
    {
        var dataSource = StriaDataSource.Create(
            StriaDataSource.RequiredConnectionString(configuration),
            configuration.GetValue<bool>(StriaDataSource.UseEntraAuthKey),
            credential);
        services.AddSingleton(dataSource);
        services.AddDbContextFactory<StriaDbContext>(options => options.UseNpgsql(dataSource));
        return services;
    }
}
