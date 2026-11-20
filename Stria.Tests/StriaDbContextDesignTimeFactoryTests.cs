using Azure.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Stria.Data;

namespace Stria.Tests;

/// <summary>
/// The migrations bundle builds its context here, not from Program.cs. No database or Azure
/// needed: building a context never connects, and the credential throws if asked for a token.
/// </summary>
[TestClass]
public sealed class StriaDbContextDesignTimeFactoryTests
{
    [TestMethod]
    public void MissingConnectionString_FailsWithSettingName()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Create([], out _));

        Assert.IsTrue(ex.Message.Contains("ConnectionStrings__Stria", StringComparison.Ordinal), ex.Message);
    }

    [TestMethod]
    public void EntraAuth_WithPassword_IsRejectedWithoutEchoingIt()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Create(
        [
            ("ConnectionStrings:Stria", "Host=db;Database=stria;Username=stria;Password=leftover-secret"),
            (StriaDataSource.UseEntraAuthKey, "true")
        ], out _));

        Assert.IsTrue(ex.Message.Contains("must not contain a password", StringComparison.Ordinal), ex.Message);
        Assert.IsFalse(ex.Message.Contains("leftover-secret", StringComparison.Ordinal), "Values must not be echoed.");
    }

    [TestMethod]
    public void EntraAuth_Passwordless_BuildsNpgsqlContextWithoutRequestingToken()
    {
        using var db = Create(
        [
            ("ConnectionStrings:Stria", "Host=db;Database=stria;Username=stria-identity"),
            (StriaDataSource.UseEntraAuthKey, "true")
        ], out var credential);

        Assert.IsTrue(db.Database.IsNpgsql());
        Assert.IsFalse(credential.IsValueCreated, "Building the context must not fetch a token.");
    }

    private static StriaDbContext Create((string Key, string Value)[] settings, out Lazy<TokenCredential> credential)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();
        credential = new Lazy<TokenCredential>(() => throw new AssertFailedException("No token may be requested."));
        return StriaDbContextDesignTimeFactory.Create(configuration, credential);
    }
}
