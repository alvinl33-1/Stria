using Microsoft.EntityFrameworkCore;
using Stria.Data;
using Testcontainers.PostgreSql;

namespace Stria.Tests.TestSupport;

/// <summary>
/// One disposable PostgreSQL container per integration test class. The real provider is used
/// deliberately: the schema relies on a partial unique index, check constraints and
/// <c>ExecuteUpdateAsync</c>, none of which an in-memory provider reproduces faithfully.
/// </summary>
public sealed class PostgresFixture : IAsyncDisposable
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("stria_tests")
        .WithUsername("stria")
        .WithPassword("stria_tests_password")
        .Build();

    private IDbContextFactory<StriaDbContext> _contextFactory = null!;

    public string ConnectionString { get; private set; } = string.Empty;

    public IDbContextFactory<StriaDbContext> ContextFactory =>
        _contextFactory ?? throw new InvalidOperationException("StartAsync has not been called.");

    public async Task StartAsync()
    {
        await _container.StartAsync();

        ConnectionString = _container.GetConnectionString();
        var options = new DbContextOptionsBuilder<StriaDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        _contextFactory = new SimpleDbContextFactory(options);

        await using var db = await _contextFactory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
    }

    /// <summary>Removes all experiment and kill-switch rows so each test starts from a known state.</summary>
    public async Task ResetAsync()
    {
        await using var db = await ContextFactory.CreateDbContextAsync();
        await db.ChaosExperiments.ExecuteDeleteAsync();
        await db.KillSwitchEvents.ExecuteDeleteAsync();
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    private sealed class SimpleDbContextFactory(DbContextOptions<StriaDbContext> options)
        : IDbContextFactory<StriaDbContext>
    {
        public StriaDbContext CreateDbContext() => new(options);
    }
}

/// <summary>
/// Docker is required for the integration tests. When it is missing the tests report
/// inconclusive instead of failing the whole suite.
/// </summary>
public static class DockerRequirement
{
    public static bool TryStart(PostgresFixture fixture, out string? failureReason)
    {
        try
        {
            // Blocking here is fine: [ClassInitialize] is already async-friendly and this is
            // the single point where the container is started.
            fixture.StartAsync().GetAwaiter().GetResult();
            failureReason = null;
            return true;
        }
        catch (Exception ex)
        {
            failureReason = $"Docker/PostgreSQL test container unavailable: {ex.Message}";
            if (string.Equals(
                    Environment.GetEnvironmentVariable("STRIA_REQUIRE_DOCKER_TESTS"),
                    "true",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(failureReason, ex);
            }

            return false;
        }
    }

    public static void SkipIfUnavailable(string? failureReason) =>
        Assert.Inconclusive(failureReason ?? "Integration test fixture was not initialized.");
}
