using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Stria.Client.Security;
using Stria.Domain;
using Stria.DTOs;
using Stria.Tests.TestSupport;

namespace Stria.Tests;

/// <summary>
/// Black-box HTTP tests for the chaos control-plane API.
/// Layering for interviews:
/// 1) ChaosRulesTests — pure domain rules (fast, no I/O)
/// 2) DatabaseChaosOrchestratorTests — persistence + Postgres constraints
/// 3) ChaosApiTests — HTTP contract the dashboard and sample service rely on
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class ChaosApiTests
{
    private static PostgresFixture _postgres = null!;
    private static StriaApiFactory _factory = null!;
    private static HttpClient _client = null!;
    private static HttpClient _dataPlane = null!;
    private static bool _ready;
    private static string? _failure;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _postgres = new PostgresFixture();
        if (!DockerRequirement.TryStart(_postgres, out _failure))
            return;

        _factory = new StriaApiFactory(_postgres);
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        // These tests cover the API contract, so they call it as an operator automation client.
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestTokens.Create(ChaosRoles.Operator));
        // Directives are read the way the sample service reads them: with its data-plane key.
        _dataPlane = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        _dataPlane.DefaultRequestHeaders.Add(Stria.Security.DataPlaneDefaults.HeaderName, TestDataPlaneKeys.PaymentGateway);
        _ready = true;
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _client?.Dispose();
        _dataPlane?.Dispose();
        _factory?.Dispose();
        if (_postgres is not null)
            _postgres.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [TestInitialize]
    public async Task TestInit()
    {
        if (!_ready)
            Assert.Inconclusive(_failure ?? "Docker/PostgreSQL test container unavailable.");

        await _postgres.ResetAsync();
    }

    [TestMethod]
    public async Task Healthz_ReturnsOk()
    {
        var response = await _client.GetAsync("/healthz");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task Readyz_ReturnsOk_WhenDatabaseIsUp()
    {
        var response = await _client.GetAsync("/readyz");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task Inject_ValidLatency_ReturnsAccepted_AndAppearsInFaults()
    {
        var payload = new FaultInjectionRequest(
            TargetService: "PaymentGateway",
            FaultType: "Latency",
            LatencyMs: 250,
            DurationSeconds: 120,
            Percent: 40);

        var inject = await _client.PostAsJsonAsync("/api/v1/chaos/inject", payload);
        Assert.AreEqual(HttpStatusCode.Accepted, inject.StatusCode);

        var faults = await _client.GetFromJsonAsync<List<ChaosExperiment>>("/api/v1/chaos/faults", JsonOptions);
        Assert.IsNotNull(faults);
        Assert.HasCount(1, faults);
        Assert.AreEqual("PaymentGateway", faults[0].TargetService);
        Assert.AreEqual("Latency", faults[0].FaultType);
        Assert.AreEqual(250, faults[0].LatencyMs);
        Assert.AreEqual(40, faults[0].Percent);
        Assert.AreEqual(TestTokens.DefaultObjectId, faults[0].TriggeredBy);
        Assert.AreEqual(TestTokens.DisplayName, faults[0].TriggeredByName);
    }

    [TestMethod]
    public async Task Inject_InvalidTarget_ReturnsBadRequest()
    {
        var payload = new FaultInjectionRequest(
            TargetService: "NotAService",
            FaultType: "Latency",
            LatencyMs: 100);

        var response = await _client.PostAsJsonAsync("/api/v1/chaos/inject", payload);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);

        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("Invalid fault injection request.", doc.RootElement.GetProperty("title").GetString());
        Assert.AreEqual("about:blank", doc.RootElement.GetProperty("type").GetString());
        Assert.AreEqual("Unknown target service.", doc.RootElement.GetProperty("detail").GetString());
    }

    [TestMethod]
    public async Task Inject_OutOfBoundsLatency_ReturnsProblemDetails()
    {
        var payload = new FaultInjectionRequest(
            TargetService: "PaymentGateway",
            FaultType: "Latency",
            LatencyMs: ChaosRules.MaxLatencyMs + 1);

        var response = await _client.PostAsJsonAsync("/api/v1/chaos/inject", payload);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("Invalid fault injection request.", doc.RootElement.GetProperty("title").GetString());
        Assert.AreEqual("about:blank", doc.RootElement.GetProperty("type").GetString());
    }

    [TestMethod]
    public async Task Directives_NoActiveFault_ReturnsNoContent()
    {
        var response = await _dataPlane.GetAsync("/api/v1/chaos/directives/PaymentGateway");
        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
    }

    [TestMethod]
    public async Task Directives_ExpiredLease_ReturnsNoContent()
    {
        var now = DateTime.UtcNow;
        await using (var db = await _postgres.ContextFactory.CreateDbContextAsync())
        {
            db.ChaosExperiments.Add(new ChaosExperiment
            {
                TargetService = "PaymentGateway",
                FaultType = "Latency",
                LatencyMs = 100,
                Percent = 100,
                Status = ExperimentStatus.Active,
                TriggeredBy = "test-actor",
                CreatedAt = now.AddMinutes(-2),
                ExpiresAt = now.AddMinutes(-1)
            });
            await db.SaveChangesAsync();
        }

        var response = await _dataPlane.GetAsync("/api/v1/chaos/directives/PaymentGateway");

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
    }

    [TestMethod]
    public async Task Directives_ActiveFault_ReturnsDirectiveWithRemainingLease()
    {
        var inject = await _client.PostAsJsonAsync(
            "/api/v1/chaos/inject",
            new FaultInjectionRequest(
                TargetService: "PaymentGateway",
                FaultType: "HttpFailure",
                StatusCode: 502,
                DurationSeconds: 90,
                Percent: 100));
        Assert.AreEqual(HttpStatusCode.Accepted, inject.StatusCode);

        var response = await _dataPlane.GetAsync("/api/v1/chaos/directives/PaymentGateway");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var directive = await response.Content.ReadFromJsonAsync<FaultDirective>(JsonOptions);
        Assert.IsNotNull(directive);
        Assert.AreEqual("HttpFailure", directive.FaultType);
        Assert.AreEqual(502, directive.StatusCode);
        Assert.IsTrue(directive.RemainingSeconds is > 0 and <= 90);
    }

    [TestMethod]
    public async Task Inject_None_ClearsActiveFault()
    {
        Assert.AreEqual(
            HttpStatusCode.Accepted,
            (await _client.PostAsJsonAsync(
                "/api/v1/chaos/inject",
                new FaultInjectionRequest("PaymentGateway", "Latency", LatencyMs: 100))).StatusCode);

        Assert.AreEqual(
            HttpStatusCode.Accepted,
            (await _client.PostAsJsonAsync(
                "/api/v1/chaos/inject",
                new FaultInjectionRequest("PaymentGateway", "None"))).StatusCode);

        var faults = await _client.GetFromJsonAsync<List<ChaosExperiment>>("/api/v1/chaos/faults", JsonOptions);
        Assert.IsNotNull(faults);
        Assert.HasCount(0, faults);

        Assert.AreEqual(
            HttpStatusCode.NoContent,
            (await _dataPlane.GetAsync("/api/v1/chaos/directives/PaymentGateway")).StatusCode);
    }
}
