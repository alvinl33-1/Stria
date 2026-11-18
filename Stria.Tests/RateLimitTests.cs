using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Stria.Client.Security;
using Stria.DTOs;
using Stria.Tests.TestSupport;

namespace Stria.Tests;

/// <summary>Uses its own host with a tiny mutation limit, so other classes never see 429.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class RateLimitTests
{
    private static ApiTestHost _host = null!;

    [ClassInitialize]
    public static void ClassInit(TestContext _) => _host = new ApiTestHost(new Dictionary<string, string?>
    {
        ["RateLimits:MutatePermitLimit"] = "2",
        ["RateLimits:WindowSeconds"] = "60"
    });

    [ClassCleanup]
    public static void ClassCleanup() => _host?.Dispose();

    [TestInitialize]
    public Task TestInit() => _host.ResetAsync();

    [TestMethod]
    public async Task Mutations_OverLimit_Return429WithRetryAfter_PerCaller()
    {
        using var first = Operator("66666666-6666-6666-6666-666666666666");
        var request = new FaultInjectionRequest("PaymentGateway", "None");

        Assert.AreEqual(HttpStatusCode.Accepted, (await first.PostAsJsonAsync("/api/v1/chaos/inject", request)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Accepted, (await first.PostAsJsonAsync("/api/v1/chaos/inject", request)).StatusCode);

        var limited = await first.PostAsJsonAsync("/api/v1/chaos/inject", request);
        Assert.AreEqual(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.IsNotNull(limited.Headers.RetryAfter);
        Assert.AreEqual("application/problem+json", limited.Content.Headers.ContentType?.MediaType);

        // A different operator has their own budget.
        using var second = Operator("77777777-7777-7777-7777-777777777777");
        Assert.AreEqual(HttpStatusCode.Accepted, (await second.PostAsJsonAsync("/api/v1/chaos/inject", request)).StatusCode);
    }

    private static HttpClient Operator(string oid)
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokens.Create(ChaosRoles.Operator, oid));
        return client;
    }
}
