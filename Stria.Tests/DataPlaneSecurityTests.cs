using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Stria.Client.Security;
using Stria.DTOs;
using Stria.Security;
using Stria.Tests.TestSupport;

namespace Stria.Tests;

/// <summary>Data-plane credential separation: own directive only, and no crossover with operator credentials.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class DataPlaneSecurityTests
{
    private const string PaymentGatewayDirective = "/api/v1/chaos/directives/PaymentGateway";
    private static ApiTestHost _host = null!;

    [ClassInitialize]
    public static void ClassInit(TestContext _) => _host = new ApiTestHost();

    [ClassCleanup]
    public static void ClassCleanup() => _host?.Dispose();

    [TestInitialize]
    public Task TestInit() => _host.ResetAsync();

    private static HttpClient KeyClient(ApiTestHost host, string? key)
    {
        var client = host.CreateClient();
        if (key is not null)
            client.DefaultRequestHeaders.Add(DataPlaneDefaults.HeaderName, key);
        return client;
    }

    [TestMethod]
    public async Task Directives_NoKey_Returns401()
    {
        using var client = KeyClient(_host, null);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync(PaymentGatewayDirective)).StatusCode);
    }

    [TestMethod]
    public async Task Directives_WrongKey_Returns401()
    {
        using var client = KeyClient(_host, "not-a-real-key");
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync(PaymentGatewayDirective)).StatusCode);
    }

    [TestMethod]
    public async Task Directives_OwnKey_NoFault_Returns204()
    {
        using var client = KeyClient(_host, TestDataPlaneKeys.PaymentGateway);
        Assert.AreEqual(HttpStatusCode.NoContent, (await client.GetAsync(PaymentGatewayDirective)).StatusCode);
    }

    [TestMethod]
    public async Task Directives_OwnKey_ActiveFault_Returns200()
    {
        using var operatorClient = _host.CreateClient();
        operatorClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokens.Create(ChaosRoles.Operator));
        var inject = await operatorClient.PostAsJsonAsync("/api/v1/chaos/inject",
            new FaultInjectionRequest("PaymentGateway", "Latency", LatencyMs: 100, DurationSeconds: 60));
        Assert.AreEqual(HttpStatusCode.Accepted, inject.StatusCode);

        using var client = KeyClient(_host, TestDataPlaneKeys.PaymentGateway);
        var response = await client.GetAsync(PaymentGatewayDirective);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var directive = await response.Content.ReadFromJsonAsync<FaultDirective>();
        Assert.AreEqual("Latency", directive?.FaultType);
    }

    [TestMethod]
    public async Task Directives_AnotherServicesKey_Returns403()
    {
        using var client = KeyClient(_host, TestDataPlaneKeys.AuthService);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.GetAsync(PaymentGatewayDirective)).StatusCode);
    }

    [TestMethod]
    public async Task Directives_UnknownService_Returns403()
    {
        using var client = KeyClient(_host, TestDataPlaneKeys.PaymentGateway);
        Assert.AreEqual(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/v1/chaos/directives/GhostService")).StatusCode);
    }

    [TestMethod]
    [DataRow(ChaosRoles.Viewer)]
    [DataRow(ChaosRoles.Operator)]
    [DataRow(ChaosRoles.Admin)]
    public async Task Directives_OperatorBearerToken_Returns401(string role)
    {
        using var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokens.Create(role));
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync(PaymentGatewayDirective)).StatusCode);
    }

    [TestMethod]
    public async Task ControlPlane_DataPlaneKey_Returns401()
    {
        using var client = KeyClient(_host, TestDataPlaneKeys.PaymentGateway);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/chaos/faults")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/chaos/inject",
            new FaultInjectionRequest("PaymentGateway", "None"))).StatusCode);
    }

    [TestMethod]
    public async Task Directives_OverServiceLimit_Returns429()
    {
        using var host = new ApiTestHost(new Dictionary<string, string?> { ["RateLimits:DataPlanePermitLimit"] = "2" });
        await host.ResetAsync();
        using var client = KeyClient(host, TestDataPlaneKeys.PaymentGateway);

        Assert.AreEqual(HttpStatusCode.NoContent, (await client.GetAsync(PaymentGatewayDirective)).StatusCode);
        Assert.AreEqual(HttpStatusCode.NoContent, (await client.GetAsync(PaymentGatewayDirective)).StatusCode);
        var limited = await client.GetAsync(PaymentGatewayDirective);
        Assert.AreEqual(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.IsTrue(limited.Headers.Contains("Retry-After"));
    }
}
