using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Stria.Client.Security;
using Stria.Client.Services;
using Stria.DTOs;
using Stria.Tests.TestSupport;

namespace Stria.Tests;

/// <summary>Antiforgery, request-size limit and security headers.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class HardeningTests
{
    private static ApiTestHost _host = null!;

    private static readonly FaultInjectionRequest ValidInjection =
        new("AuthService", "HttpFailure", StatusCode: 503, DurationSeconds: 60);

    [ClassInitialize]
    public static void ClassInit(TestContext _) => _host = new ApiTestHost();

    [ClassCleanup]
    public static void ClassCleanup() => _host?.Dispose();

    [TestInitialize]
    public Task TestInit() => _host.ResetAsync();

    private static HttpClient CookieOperator()
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Add(
            TestCookieHandler.Header, $"55555555-5555-5555-5555-555555555555|{ChaosRoles.Operator}");
        return client;
    }

    [TestMethod]
    public async Task CookiePost_WithoutAntiforgeryToken_Returns400ProblemDetails()
    {
        using var client = CookieOperator();
        var response = await client.PostAsJsonAsync("/api/v1/chaos/inject", ValidInjection);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("Missing or invalid antiforgery token.", doc.RootElement.GetProperty("title").GetString());
    }

    [TestMethod]
    public async Task CookiePost_WithAntiforgeryToken_IsAccepted()
    {
        using var client = CookieOperator();
        // The dashboard's own client: fetches a token (cookie stored by the client), then posts.
        var api = new ChaosApiClient(client);

        using var response = await api.PostAsJsonAsync("/api/v1/chaos/inject", ValidInjection);

        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
    }

    [TestMethod]
    public async Task CookiePost_WithFakeBearerHeader_StillRequiresAntiforgery()
    {
        using var client = CookieOperator();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-token");

        var response = await client.PostAsJsonAsync("/api/v1/chaos/inject", ValidInjection);

        // The forged bearer token fails, the cookie identity authorizes, and antiforgery still applies.
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task BearerPost_WithoutAntiforgeryToken_IsAccepted()
    {
        using var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokens.Create(ChaosRoles.Operator));

        var response = await client.PostAsJsonAsync("/api/v1/chaos/inject", ValidInjection);

        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
    }

    [TestMethod]
    public async Task OversizedBody_Returns413()
    {
        using var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokens.Create(ChaosRoles.Operator));

        var padding = new string('x', 17 * 1024);
        using var content = new StringContent(
            $$"""{"targetService":"PaymentGateway","faultType":"Latency","latencyMs":100,"triggeredBy":"{{padding}}"}""",
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync("/api/v1/chaos/inject", content);

        Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [TestMethod]
    public async Task Responses_CarrySecurityHeaders()
    {
        using var client = _host.CreateClient();
        var response = await client.GetAsync("/healthz");

        Assert.AreEqual("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.AreEqual("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.IsTrue(response.Headers.Contains("Permissions-Policy"));
        StringAssert.Contains(
            response.Headers.GetValues("Content-Security-Policy-Report-Only").Single(),
            "frame-ancestors 'none'");
    }
}
