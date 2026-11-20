using Stria.Endpoints;

namespace Stria.Tests;

/// <summary>
/// The open-redirect guard on sign-in and sign-out. Anything that is not a same-site path
/// becomes "/".
/// </summary>
[TestClass]
public sealed class AuthenticationEndpointsTests
{
    [TestMethod]
    [DataRow("/")]
    [DataRow("/dashboard")]
    [DataRow("/experiments?target=PaymentGateway")]
    public void LocalUrl_KeepsSameSitePaths(string url) =>
        Assert.AreEqual(url, AuthenticationEndpoints.LocalUrl(url));

    [TestMethod]
    [DataRow((string?)null)]
    [DataRow("")]
    [DataRow("dashboard")]
    [DataRow("https://evil.example/")]
    [DataRow("//evil.example/")]
    [DataRow("/\\evil.example/")]
    [DataRow("/\t/evil.example/")]
    [DataRow("/\n/evil.example/")]
    public void LocalUrl_ReplacesOffSiteOrMalformedTargetsWithRoot(string? url) =>
        Assert.AreEqual("/", AuthenticationEndpoints.LocalUrl(url));
}
