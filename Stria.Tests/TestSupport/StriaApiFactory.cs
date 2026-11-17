using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Stria;

namespace Stria.Tests.TestSupport;

/// <summary>
/// Boots the real Stria host against a Testcontainers PostgreSQL database.
/// Interview talking point: WebApplicationFactory runs Program.cs in-process so you exercise
/// middleware + routing + DI without publishing a container for every test.
/// </summary>
public sealed class StriaApiFactory : WebApplicationFactory<Program>
{
    private readonly PostgresFixture _postgres;
    private readonly string _dpKeysPath;

    public StriaApiFactory(PostgresFixture postgres)
    {
        _postgres = postgres;
        _dpKeysPath = Path.Combine(Path.GetTempPath(), "stria-tests-dpkeys", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dpKeysPath);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // UseSetting values flow into IConfiguration before Program.cs reads ConnectionStrings:Stria.
        builder.UseSetting("ConnectionStrings:Stria", _postgres.ConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "false"); // schema already applied by PostgresFixture
        builder.UseSetting("DataProtection:KeysPath", _dpKeysPath);
        builder.UseSetting("Expiry:SweepIntervalSeconds", "3600");
        builder.UseSetting("Retention:SweepIntervalMinutes", "60");

        builder.ConfigureServices(services =>
        {
            // Drop the noisy demo traffic logger in API tests; keep expiry/retention registered.
            var traffic = services.FirstOrDefault(d =>
                d.ServiceType == typeof(IHostedService) &&
                d.ImplementationType == typeof(TrafficSimulatorService));
            if (traffic is not null)
                services.Remove(traffic);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            try
            {
                if (Directory.Exists(_dpKeysPath))
                    Directory.Delete(_dpKeysPath, recursive: true);
            }
            catch
            {
                // Best-effort temp cleanup; test hosts may still hold a file handle briefly.
            }
        }
    }
}
