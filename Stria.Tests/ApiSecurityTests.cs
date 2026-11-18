using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Stria.Client.Security;
using Stria.DTOs;
using Stria.Tests.TestSupport;

namespace Stria.Tests;

/// <summary>
/// Authentication and the authorization matrix for the control-plane API, over both schemes:
/// bearer tokens (signed with the test key) and the browser cookie (test handler).
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class ApiSecurityTests
{
    private const string Anonymous = "anonymous";
    private const string NoRole = "no-role";

    private static ApiTestHost _host = null!;

    private static readonly FaultInjectionRequest ValidInjection =
        new("PaymentGateway", "Latency", LatencyMs: 100, DurationSeconds: 60);

    [ClassInitialize]
    public static void ClassInit(TestContext _) => _host = new ApiTestHost();

    [ClassCleanup]
    public static void ClassCleanup() => _host?.Dispose();

    [TestInitialize]
    public Task TestInit() => _host.ResetAsync();

    private static HttpClient BearerClient(string caller, string oid = TestTokens.DefaultObjectId)
    {
        var client = _host.CreateClient();
        if (caller != Anonymous)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", TestTokens.Create(caller == NoRole ? null : caller, oid));
        return client;
    }

    private static HttpClient CookieClient(string? role, string oid = "22222222-2222-2222-2222-222222222222")
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Add(TestCookieHandler.Header, $"{oid}|{role}");
        return client;
    }

    // ---- Authentication ------------------------------------------------------------------

    [TestMethod]
    public async Task Me_Anonymous_Returns401_NotARedirect()
    {
        using var client = BearerClient(Anonymous);
        var response = await client.GetAsync("/api/v1/me");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Me_ValidBearerToken_ReturnsCallerObjectId()
    {
        using var client = BearerClient(ChaosRoles.Viewer, oid: "33333333-3333-3333-3333-333333333333");
        var response = await client.GetAsync("/api/v1/me");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<MeResponse>();
        Assert.AreEqual("33333333-3333-3333-3333-333333333333", me?.Oid);
    }

    [TestMethod]
    public async Task Me_ExpiredToken_Returns401()
    {
        using var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokens.Create(ChaosRoles.Viewer, expires: DateTime.UtcNow.AddMinutes(-10)));

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
    }

    [TestMethod]
    public async Task Me_WrongAudience_Returns401()
    {
        using var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokens.Create(ChaosRoles.Viewer, audience: "api://some-other-app"));

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
    }

    [TestMethod]
    public async Task Me_CookieUser_Returns200()
    {
        using var client = CookieClient(ChaosRoles.Viewer);
        Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync("/api/v1/me")).StatusCode);
    }

    // ---- Authorization matrix ------------------------------------------------------------

    [TestMethod]
    [DataRow(Anonymous, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized)]
    [DataRow(NoRole, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
    [DataRow(ChaosRoles.Viewer, HttpStatusCode.OK, HttpStatusCode.Forbidden)]
    [DataRow(ChaosRoles.Operator, HttpStatusCode.OK, HttpStatusCode.Accepted)]
    [DataRow(ChaosRoles.Admin, HttpStatusCode.OK, HttpStatusCode.Accepted)]
    public async Task Bearer_Matrix(string caller, HttpStatusCode faults, HttpStatusCode inject)
    {
        using var client = BearerClient(caller);

        Assert.AreEqual(faults, (await client.GetAsync("/api/v1/chaos/faults")).StatusCode);
        Assert.AreEqual(inject, (await client.PostAsJsonAsync("/api/v1/chaos/inject", ValidInjection)).StatusCode);
    }

    [TestMethod]
    [DataRow(null, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
    [DataRow(ChaosRoles.Viewer, HttpStatusCode.OK, HttpStatusCode.Forbidden)]
    public async Task Cookie_Matrix(string? role, HttpStatusCode faults, HttpStatusCode inject)
    {
        // Authorization runs before antiforgery, so a forbidden cookie caller gets 403, not 400.
        using var client = CookieClient(role);

        Assert.AreEqual(faults, (await client.GetAsync("/api/v1/chaos/faults")).StatusCode);
        Assert.AreEqual(inject, (await client.PostAsJsonAsync("/api/v1/chaos/inject", ValidInjection)).StatusCode);
    }

    [TestMethod]
    public async Task Directives_StayAnonymous_UntilDataPlaneAuth()
    {
        using var client = BearerClient(Anonymous);
        var response = await client.GetAsync("/api/v1/chaos/directives/PaymentGateway");
        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
    }

    [TestMethod]
    public async Task Health_IsAnonymous()
    {
        using var client = BearerClient(Anonymous);
        Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync("/healthz")).StatusCode);
    }

    // ---- Trusted actor -------------------------------------------------------------------

    [TestMethod]
    public async Task Inject_IgnoresClientTriggeredBy_AndRecordsTokenIdentity()
    {
        const string oid = "44444444-4444-4444-4444-444444444444";
        using var client = BearerClient(ChaosRoles.Operator, oid);

        var response = await client.PostAsJsonAsync(
            "/api/v1/chaos/inject",
            ValidInjection with { TriggeredBy = "mallory" });
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);

        await using var db = await _host.Postgres.ContextFactory.CreateDbContextAsync();
        var row = await db.ChaosExperiments.AsNoTracking().SingleAsync();
        Assert.AreEqual(oid, row.TriggeredBy);
        Assert.AreEqual(TestTokens.DisplayName, row.TriggeredByName);
    }

    private sealed record MeResponse(string? Oid, string? Name);
}
