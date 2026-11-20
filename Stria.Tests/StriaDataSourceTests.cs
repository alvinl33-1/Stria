using Azure.Core;
using Npgsql;
using Stria.Data;
using Stria.Tests.TestSupport;

namespace Stria.Tests;

/// <summary>
/// Entra auth against a real PostgreSQL: the connection string carries no password, and a fake
/// credential returns the container's password as the "token". A successful query proves the
/// token, not the connection string, authenticated the connection.
/// </summary>
[TestClass]
public sealed class StriaDataSourceTests
{
    private static PostgresFixture _postgres = null!;
    private static string? _failure;
    private static bool _ready;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _postgres = new PostgresFixture();
        _ready = DockerRequirement.TryStart(_postgres, out _failure);
    }

    [ClassCleanup]
    public static void ClassCleanup() => _postgres?.DisposeAsync().AsTask().GetAwaiter().GetResult();

    [TestInitialize]
    public void TestInit()
    {
        if (!_ready)
            DockerRequirement.SkipIfUnavailable(_failure);
    }

    [TestMethod]
    public async Task EntraAuth_SendsTokenForPostgresScope_AsPassword()
    {
        var connection = new NpgsqlConnectionStringBuilder(_postgres.ConnectionString);
        var credential = new PasswordAsTokenCredential(connection.Password!);
        connection.Password = null;

        await using var dataSource = StriaDataSource.Create(
            connection.ConnectionString, useEntraAuth: true, new Lazy<TokenCredential>(() => credential));
        await using var command = dataSource.CreateCommand("SELECT 1");

        Assert.AreEqual(1, await command.ExecuteScalarAsync());
        CollectionAssert.Contains(credential.RequestedScopes, StriaDataSource.PostgresTokenScope);
    }

    [TestMethod]
    public async Task PasswordAuth_NeverCreatesCredential()
    {
        var credential = new Lazy<TokenCredential>(() => throw new AssertFailedException("Password auth must not build a credential."));

        await using var dataSource = StriaDataSource.Create(_postgres.ConnectionString, useEntraAuth: false, credential);
        await using var command = dataSource.CreateCommand("SELECT 1");

        Assert.AreEqual(1, await command.ExecuteScalarAsync());
        Assert.IsFalse(credential.IsValueCreated);
    }

    [TestMethod]
    public async Task PasswordAuth_WithoutPassword_IsRejected()
    {
        var connection = new NpgsqlConnectionStringBuilder(_postgres.ConnectionString) { Password = null };
        var credential = new Lazy<TokenCredential>(() => new PasswordAsTokenCredential("unused"));

        await using var dataSource = StriaDataSource.Create(connection.ConnectionString, useEntraAuth: false, credential);
        await using var command = dataSource.CreateCommand("SELECT 1");

        await Assert.ThrowsAsync<NpgsqlException>(() => command.ExecuteScalarAsync());
    }

    private sealed class PasswordAsTokenCredential(string password) : TokenCredential
    {
        public List<string> RequestedScopes { get; } = [];

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            RequestedScopes.AddRange(requestContext.Scopes);
            return new AccessToken(password, DateTimeOffset.UtcNow.AddHours(1));
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }
}
