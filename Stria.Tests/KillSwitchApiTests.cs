using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Stria.Client.Security;
using Stria.Client.Services;
using Stria.Domain;
using Stria.DTOs;
using Stria.Security;
using Stria.Tests.TestSupport;

namespace Stria.Tests;

/// <summary>
/// The kill-switch column of the Phase 2 authorization matrix, plus <c>409</c> suppression,
/// antiforgery and validation for the engage/release endpoints.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class KillSwitchApiTests
{
    private const string Engage = "/api/v1/chaos/kill-switch/engage";
    private const string Release = "/api/v1/chaos/kill-switch/release";
    private const string State = "/api/v1/chaos/kill-switch";

    private static readonly KillSwitchRequest ValidRequest = new("incident 42");
    private static readonly FaultInjectionRequest ValidInjection =
        new("PaymentGateway", "Latency", LatencyMs: 100, DurationSeconds: 60);

    private static ApiTestHost _host = null!;

    [ClassInitialize]
    public static void ClassInit(TestContext _) => _host = new ApiTestHost();

    [ClassCleanup]
    public static void ClassCleanup() => _host?.Dispose();

    [TestInitialize]
    public Task TestInit() => _host.ResetAsync();

    private static HttpClient BearerClient(string? role, string oid = TestTokens.DefaultObjectId)
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokens.Create(role, oid));
        return client;
    }

    private static HttpClient CookieClient(string role)
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Add(TestCookieHandler.Header, $"66666666-6666-6666-6666-666666666666|{role}");
        return client;
    }

    private static async Task EngageAsAdminAsync()
    {
        using var admin = BearerClient(ChaosRoles.Admin);
        var response = await admin.PostAsJsonAsync(Engage, ValidRequest);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- Authorization matrix ------------------------------------------------------------

    [TestMethod]
    [DataRow(Engage)]
    [DataRow(Release)]
    public async Task Change_Anonymous_Returns401(string url)
    {
        using var client = _host.CreateClient();
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(url, ValidRequest)).StatusCode);
    }

    [TestMethod]
    [DataRow(Engage, ChaosRoles.Viewer)]
    [DataRow(Engage, ChaosRoles.Operator)]
    [DataRow(Release, ChaosRoles.Viewer)]
    [DataRow(Release, ChaosRoles.Operator)]
    public async Task Change_NonAdmin_Returns403(string url, string role)
    {
        using var client = BearerClient(role);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(url, ValidRequest)).StatusCode);
    }

    [TestMethod]
    public async Task Change_CookieOperator_Returns403_BeforeAntiforgery()
    {
        using var client = CookieClient(ChaosRoles.Operator);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Engage, ValidRequest)).StatusCode);
    }

    [TestMethod]
    [DataRow(Engage)]
    [DataRow(Release)]
    public async Task Change_DataPlaneKey_Returns401(string url)
    {
        using var client = _host.CreateClient();
        client.DefaultRequestHeaders.Add(DataPlaneDefaults.HeaderName, TestDataPlaneKeys.PaymentGateway);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(url, ValidRequest)).StatusCode);
    }

    [TestMethod]
    public async Task Change_ExpiredOrWrongAudienceToken_Returns401()
    {
        foreach (var token in new[]
                 {
                     TestTokens.Create(ChaosRoles.Admin, expires: DateTime.UtcNow.AddMinutes(-10)),
                     TestTokens.Create(ChaosRoles.Admin, audience: "api://some-other-app")
                 })
        {
            using var client = _host.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Engage, ValidRequest)).StatusCode);
        }
    }

    [TestMethod]
    public async Task Get_Viewer_Returns200_Anonymous_Returns401()
    {
        using var viewer = BearerClient(ChaosRoles.Viewer);
        using var anonymous = _host.CreateClient();

        Assert.AreEqual(HttpStatusCode.OK, (await viewer.GetAsync(State)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(State)).StatusCode);
    }

    // ---- Behaviour ------------------------------------------------------------------------

    [TestMethod]
    public async Task Engage_Admin_ReportsStateWithActorFromClaims()
    {
        using var admin = BearerClient(ChaosRoles.Admin, oid: "77777777-7777-7777-7777-777777777777");
        var response = await admin.PostAsJsonAsync(Engage, ValidRequest);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var state = await response.Content.ReadFromJsonAsync<KillSwitchState>();
        Assert.IsNotNull(state);
        Assert.IsTrue(state.Engaged);
        Assert.AreEqual("77777777-7777-7777-7777-777777777777", state.ChangedBy);
        Assert.AreEqual(TestTokens.DisplayName, state.ChangedByName);
        Assert.AreEqual("incident 42", state.Reason);

        using var viewer = BearerClient(ChaosRoles.Viewer);
        var seen = await viewer.GetFromJsonAsync<KillSwitchState>(State);
        Assert.AreEqual(state, seen);
    }

    [TestMethod]
    public async Task Inject_WhileEngaged_Returns409ProblemDetails()
    {
        await EngageAsAdminAsync();

        using var op = BearerClient(ChaosRoles.Operator);
        var response = await op.PostAsJsonAsync("/api/v1/chaos/inject", ValidInjection);

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("Fault injection is suspended by the kill switch.", doc.RootElement.GetProperty("title").GetString());
    }

    [TestMethod]
    public async Task Engage_ClearsActiveFault_AndDirectiveReturns204()
    {
        using var op = BearerClient(ChaosRoles.Operator);
        Assert.AreEqual(HttpStatusCode.Accepted, (await op.PostAsJsonAsync("/api/v1/chaos/inject", ValidInjection)).StatusCode);

        using var service = _host.CreateClient();
        service.DefaultRequestHeaders.Add(DataPlaneDefaults.HeaderName, TestDataPlaneKeys.PaymentGateway);
        Assert.AreEqual(HttpStatusCode.OK, (await service.GetAsync("/api/v1/chaos/directives/PaymentGateway")).StatusCode);

        await EngageAsAdminAsync();

        Assert.AreEqual(HttpStatusCode.NoContent, (await service.GetAsync("/api/v1/chaos/directives/PaymentGateway")).StatusCode);
        var faults = await op.GetFromJsonAsync<List<JsonElement>>("/api/v1/chaos/faults");
        Assert.IsEmpty(faults!);
    }

    [TestMethod]
    public async Task Release_Admin_AllowsInjectionAgain()
    {
        await EngageAsAdminAsync();

        using var admin = BearerClient(ChaosRoles.Admin);
        var released = await (await admin.PostAsJsonAsync(Release, new KillSwitchRequest("resolved")))
            .Content.ReadFromJsonAsync<KillSwitchState>();
        using var op = BearerClient(ChaosRoles.Operator);

        Assert.IsFalse(released!.Engaged);
        Assert.AreEqual(HttpStatusCode.Accepted, (await op.PostAsJsonAsync("/api/v1/chaos/inject", ValidInjection)).StatusCode);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("   ")]
    public async Task Engage_MissingReason_Returns400(string? reason)
    {
        using var admin = BearerClient(ChaosRoles.Admin);
        var response = await admin.PostAsJsonAsync(Engage, new KillSwitchRequest(reason));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        using var viewer = BearerClient(ChaosRoles.Viewer);
        Assert.IsFalse((await viewer.GetFromJsonAsync<KillSwitchState>(State))!.Engaged);
    }

    [TestMethod]
    public async Task Engage_CookieAdmin_RequiresAntiforgeryToken()
    {
        using var client = CookieClient(ChaosRoles.Admin);

        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Engage, ValidRequest)).StatusCode);

        using var response = await new ChaosApiClient(client).PostAsJsonAsync(Engage, ValidRequest);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task Engage_ActorFieldInBody_IsIgnored()
    {
        using var admin = BearerClient(ChaosRoles.Admin);
        var response = await admin.PostAsJsonAsync(Engage, new { reason = "incident", actor = "spoofed", changedBy = "spoofed" });

        var state = await response.Content.ReadFromJsonAsync<KillSwitchState>();
        Assert.AreEqual(TestTokens.DefaultObjectId, state!.ChangedBy);
    }
}
