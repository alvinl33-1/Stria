using Microsoft.AspNetCore.Mvc.Testing;

namespace Stria.Tests.TestSupport;

/// <summary>
/// One PostgreSQL container plus one test host for an integration test class. Wraps the
/// [ClassInitialize] / [TestInitialize] boilerplate the API test classes share.
/// </summary>
public sealed class ApiTestHost : IDisposable
{
    private readonly string? _failure;

    public ApiTestHost(IReadOnlyDictionary<string, string?>? overrides = null)
    {
        Postgres = new PostgresFixture();
        if (!DockerRequirement.TryStart(Postgres, out _failure))
            return;

        Factory = new StriaApiFactory(Postgres, overrides);
        IsReady = true;
    }

    public PostgresFixture Postgres { get; }
    public StriaApiFactory Factory { get; } = null!;
    public bool IsReady { get; }

    /// <summary>A fresh client with its own cookie jar and no redirects followed.</summary>
    public HttpClient CreateClient() =>
        Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public async Task ResetAsync()
    {
        if (!IsReady)
            Assert.Inconclusive(_failure ?? "Docker/PostgreSQL test container unavailable.");
        await Postgres.ResetAsync();
    }

    public void Dispose()
    {
        Factory?.Dispose();
        Postgres.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
