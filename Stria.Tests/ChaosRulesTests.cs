using Stria.Domain;
using Stria.DTOs;

namespace Stria.Tests;

/// <summary>
/// <c>ChaosRules</c> is a pure static class with no injected dependencies, making it the
/// easiest test target and a high-confidence smoke for the entire project's invariants.
/// </summary>
[TestClass]
public sealed class ChaosRulesTests
{
    // ── TryGetService ────────────────────────────────────────────────────────

    [TestMethod]
    public void TryGetService_Unknown_ReturnsFalse()
    {
        // Arrange / Act
        var ok = ChaosRules.TryGetService("Nobody", out var canonical);

        // Assert
        Assert.IsFalse(ok);
        Assert.IsTrue(string.IsNullOrEmpty(canonical));
    }

    [TestMethod]
    public void TryGetService_CaseInsensitive_Matches()
    {
        // Arrange / Act
        var ok1 = ChaosRules.TryGetService("paymentgateway", out var c1);
        var ok2 = ChaosRules.TryGetService("PaymentGateway", out var c2);
        var ok3 = ChaosRules.TryGetService("PAYMENTGATEWAY", out var c3);

        // Assert
        Assert.IsTrue(ok1);
        Assert.AreEqual("PaymentGateway", c1);
        Assert.IsTrue(ok2);
        Assert.AreEqual("PaymentGateway", c2);
        Assert.IsTrue(ok3);
        Assert.AreEqual("PaymentGateway", c3);
    }

    [TestMethod]
    public void TryGetService_Whitespace_ReturnsFalse()
    {
        // Arrange / Act
        var ok = ChaosRules.TryGetService("  ", out var canonical);

        // Assert
        Assert.IsFalse(ok);
        Assert.IsTrue(string.IsNullOrEmpty(canonical));
    }

    [TestMethod]
    public void TryGetService_Null_ReturnsFalse()
    {
        // Arrange / Act
        var ok = ChaosRules.TryGetService(null, out var canonical);

        // Assert
        Assert.IsFalse(ok);
        Assert.IsTrue(string.IsNullOrEmpty(canonical));
    }

    // ── TryValidate ─────────────────────────────────────────────────────────

    [TestMethod]
    public void TryValidate_NullRequest_ReturnsError()
    {
        // Arrange / Act
        var ok = ChaosRules.TryValidate(null, out var fault, out var error);

        // Assert
        Assert.IsFalse(ok);
        Assert.AreEqual("Request body is required.", error);
        Assert.AreEqual(default, fault);
    }

    [TestMethod]
    public void TryValidate_UnknownService_ReturnsError()
    {
        // Arrange
        var req = new FaultInjectionRequest(
            TargetService: "GhostService",
            FaultType: "Latency");

        // Act
        var ok = ChaosRules.TryValidate(req, out var fault, out var error);

        // Assert
        Assert.IsFalse(ok);
        Assert.AreEqual("Unknown target service.", error);
    }

    [TestMethod]
    public void TryValidate_UnknownFaultType_ReturnsError()
    {
        // Arrange
        var req = new FaultInjectionRequest(
            TargetService: "PaymentGateway",
            FaultType: "Boom",
            LatencyMs: 100);

        // Act
        var ok = ChaosRules.TryValidate(req, out var fault, out var error);

        // Assert
        Assert.IsFalse(ok);
        Assert.AreEqual("Unsupported fault type.", error);
    }

    [TestMethod]
    public void TryValidate_Latency_RequiresLatencyMsInRange()
    {
        // Arrange — Latency may set Percent; StatusCode is HttpFailure-only.
        var req = new FaultInjectionRequest(
            TargetService: "PaymentGateway",
            FaultType: "Latency",
            LatencyMs: 750,
            Percent: 30);

        // Act
        var ok = ChaosRules.TryValidate(req, out var fault, out var error);

        // Assert
        Assert.IsTrue(ok, error);
        Assert.AreEqual(750, fault.LatencyMs);
        Assert.AreEqual(30, fault.Percent);
        Assert.IsNull(fault.StatusCode);
    }

    [TestMethod]
    public void TryValidate_Latency_ZeroMs_ReturnsError()
    {
        // Arrange
        var req = new FaultInjectionRequest(
            TargetService: "PaymentGateway",
            FaultType: "Latency",
            LatencyMs: 0);

        // Act
        var ok = ChaosRules.TryValidate(req, out var fault, out var error);

        // Assert
        Assert.IsFalse(ok);
        Assert.AreEqual("Latency must be between 1 and 60000 ms for latency faults only.", error);
    }

    [TestMethod]
    public void TryValidate_Latency_TooLarge_ReturnsError()
    {
        // Arrange
        var req = new FaultInjectionRequest(
            TargetService: "PaymentGateway",
            FaultType: "Latency",
            LatencyMs: 60001);

        // Act
        var ok = ChaosRules.TryValidate(req, out var fault, out var error);

        // Assert
        Assert.IsFalse(ok);
        Assert.AreEqual("Latency must be between 1 and 60000 ms for latency faults only.", error);
    }

    [TestMethod]
    public void TryValidate_Latency_None_Rejected()
    {
        // Arrange — LatencyMs is only legal for Latency faults. Validation fails on that
        // rule before the "None takes no other parameters" check.
        var req = new FaultInjectionRequest(
            TargetService: "PaymentGateway",
            FaultType: "None",
            LatencyMs: 100);

        // Act
        var ok = ChaosRules.TryValidate(req, out var fault, out var error);

        // Assert
        Assert.IsFalse(ok);
        Assert.AreEqual("Latency must be between 1 and 60000 ms for latency faults only.", error);
    }

    [TestMethod]
    public void TryValidate_None_WithExtraParams_Rejected()
    {
        // Arrange — Duration/Percent/StatusCode on a clear must be rejected explicitly.
        var req = new FaultInjectionRequest(
            TargetService: "PaymentGateway",
            FaultType: "None",
            DurationSeconds: 60,
            Percent: 50);

        // Act
        var ok = ChaosRules.TryValidate(req, out _, out var error);

        // Assert
        Assert.IsFalse(ok);
        Assert.AreEqual("Clearing a fault (None) takes no other parameters.", error);
    }

    [TestMethod]
    public void TryValidate_None_Clears_WithoutParams()
    {
        // Arrange - just TS and FT, all optional params get defaults
        var req = new FaultInjectionRequest(
            TargetService: "PaymentGateway",
            FaultType: "None");

        // Act
        var ok = ChaosRules.TryValidate(req, out var fault, out var error);

        // Assert
        Assert.IsTrue(ok);
        Assert.AreEqual(ChaosRules.NoFault, fault.FaultType);
        Assert.IsNull(fault.LatencyMs);
        Assert.AreEqual(100, fault.Percent);
        Assert.IsNull(fault.StatusCode);
        Assert.AreEqual(TimeSpan.Zero, fault.Duration);
    }

    [TestMethod]
    public void TryValidate_Duration_Default300_WhenNotSpecified()
    {
        // Arrange
        var req = new FaultInjectionRequest(
            TargetService: "PaymentGateway",
            FaultType: "Latency",
            LatencyMs: 100);

        // Act
        var ok = ChaosRules.TryValidate(req, out var fault, out var error);

        // Assert
        Assert.IsTrue(ok);
        Assert.AreEqual(TimeSpan.FromSeconds(300), fault.Duration);
    }

    [TestMethod]
    public void TryValidate_Duration_Min1()
    {
        // Arrange
        var req = new FaultInjectionRequest(
            TargetService: "PaymentGateway",
            FaultType: "Latency",
            LatencyMs: 100,
            DurationSeconds: 1);

        // Act
        var ok = ChaosRules.TryValidate(req, out var fault, out var error);

        // Assert
        Assert.IsTrue(ok, error);
        Assert.AreEqual(TimeSpan.FromSeconds(1), fault.Duration);
    }

    [TestMethod]
    public void TryValidate_Duration_Max3600()
    {
        // Arrange
        var req = new FaultInjectionRequest(
            TargetService: "PaymentGateway",
            FaultType: "Latency",
            LatencyMs: 100,
            DurationSeconds: 3600);

        // Act
        var ok = ChaosRules.TryValidate(req, out var fault, out var error);

        // Assert
        Assert.IsTrue(ok, error);
        Assert.AreEqual(TimeSpan.FromSeconds(3600), fault.Duration);
    }

    [TestMethod]
    public void TryValidate_Duration_TooShort()
    {
        // Arrange
        var req = new FaultInjectionRequest(
            TargetService: "PaymentGateway",
            FaultType: "Latency",
            LatencyMs: 100,
            DurationSeconds: 0);

        // Act
        var ok = ChaosRules.TryValidate(req, out _, out var error);

        // Assert
        Assert.IsFalse(ok);
        Assert.AreEqual("DurationSeconds must be between 1 and 3600.", error);
    }

    [TestMethod]
    public void TryValidate_Duration_TooLong()
    {
        // Arrange
        var req = new FaultInjectionRequest(
            TargetService: "PaymentGateway",
            FaultType: "Latency",
            LatencyMs: 100,
            DurationSeconds: 3601);

        // Act
        var ok = ChaosRules.TryValidate(req, out _, out var error);

        // Assert
        Assert.IsFalse(ok);
        Assert.AreEqual("DurationSeconds must be between 1 and 3600.", error);
    }

    [TestMethod]
    public void TryValidate_Percent_Default100_WhenNotSpecified()
    {
        // Arrange
        var req = new FaultInjectionRequest(
            TargetService: "PaymentGateway",
            FaultType: "Latency",
            LatencyMs: 100);

        // Act
        var ok = ChaosRules.TryValidate(req, out var fault, out var error);

        // Assert
        Assert.IsTrue(ok);
        Assert.AreEqual(100, fault.Percent);
    }

    [TestMethod]
    public void TryValidate_Percent_Min1()
    {
        // Arrange
        var req = new FaultInjectionRequest(
            TargetService: "PaymentGateway",
            FaultType: "Latency",
            LatencyMs: 100,
            Percent: 1);

        // Act
        var ok = ChaosRules.TryValidate(req, out var fault, out var error);

        // Assert
        Assert.IsTrue(ok);
    }

    [TestMethod]
    public void TryValidate_Percent_Max100()
    {
        // Arrange
        var req = new FaultInjectionRequest(
            TargetService: "PaymentGateway",
            FaultType: "Latency",
            LatencyMs: 100,
            Percent: 100);

        // Act
        var ok = ChaosRules.TryValidate(req, out var fault, out var error);

        // Assert
        Assert.IsTrue(ok);
    }

    [TestMethod]
    public void TryValidate_Percent_TooLow()
    {
        // Arrange
        var req = new FaultInjectionRequest(
            TargetService: "PaymentGateway",
            FaultType: "Latency",
            LatencyMs: 100,
            Percent: 0);

        // Act
        var ok = ChaosRules.TryValidate(req, out _, out var error);

        // Assert
        Assert.IsFalse(ok);
        Assert.AreEqual("Percent must be between 1 and 100.", error);
    }

    [TestMethod]
    public void TryValidate_Percent_TooHigh()
    {
        // Arrange
        var req = new FaultInjectionRequest(
            TargetService: "PaymentGateway",
            FaultType: "Latency",
            LatencyMs: 100,
            Percent: 101);

        // Act
        var ok = ChaosRules.TryValidate(req, out _, out var error);

        // Assert
        Assert.IsFalse(ok);
        Assert.AreEqual("Percent must be between 1 and 100.", error);
    }

    [TestMethod]
    public void TryValidate_Percent_Allowed_OnHttpFailure()
    {
        // Arrange — Percent is legal for Latency and HttpFailure (partial blast radius).
        var req = new FaultInjectionRequest(
            TargetService: "PaymentGateway",
            FaultType: "HttpFailure",
            Percent: 30);

        // Act
        var ok = ChaosRules.TryValidate(req, out var fault, out var error);

        // Assert
        Assert.IsTrue(ok, error);
        Assert.AreEqual(30, fault.Percent);
        Assert.AreEqual(503, fault.StatusCode);
    }

    [TestMethod]
    public void TryValidate_Percent_Rejected_OnCpuSpike()
    {
        // Arrange — CpuSpike is recorded but Percent is not meaningful for it yet.
        var req = new FaultInjectionRequest(
            TargetService: "PaymentGateway",
            FaultType: "CpuSpike",
            Percent: 30);

        // Act
        var ok = ChaosRules.TryValidate(req, out _, out var error);

        // Assert
        Assert.IsFalse(ok);
        Assert.AreEqual("Percent applies to Latency and HttpFailure faults only.", error);
    }

    [TestMethod]
    public void TryValidate_StatusCode_Default503_ForHttpFailure()
    {
        // Arrange
        var req = new FaultInjectionRequest(
            TargetService: "PaymentGateway",
            FaultType: "HttpFailure",
            LatencyMs: null);

        // Act
        var ok = ChaosRules.TryValidate(req, out var fault, out var error);

        // Assert
        Assert.IsTrue(ok);
        Assert.AreEqual(503, fault.StatusCode);
    }

    [TestMethod]
    public void TryValidate_StatusCode_NotHttpFailure_ReturnsError()
    {
        // Arrange
        var req = new FaultInjectionRequest(
            TargetService: "PaymentGateway",
            FaultType: "Latency",
            LatencyMs: 100,
            StatusCode: 500);

        // Act
        var ok = ChaosRules.TryValidate(req, out var fault, out var error);

        // Assert
        Assert.IsFalse(ok);
        Assert.AreEqual("StatusCode applies to HttpFailure faults only.", error);
    }

    [TestMethod]
    public void TryValidate_CanonicalServiceName()
    {
        // Arrange / Act
        var req = new FaultInjectionRequest(
            TargetService: "PaymentGateway",
            FaultType: "Latency",
            LatencyMs: 100);

        // Act
        var ok = ChaosRules.TryValidate(req, out var fault, out var error);

        // Assert
        Assert.IsTrue(ok);
        Assert.AreEqual("PaymentGateway", fault.TargetService);
    }

    [TestMethod]
    public void TryValidate_CaseInsensitiveServiceName()
    {
        // Arrange
        var req = new FaultInjectionRequest(
            TargetService: "paymentgateway",
            FaultType: "Latency",
            LatencyMs: 100);

        // Act
        var ok = ChaosRules.TryValidate(req, out var fault, out var error);

        // Assert
        Assert.IsTrue(ok);
        Assert.AreEqual("PaymentGateway", fault.TargetService);
    }

    [TestMethod]
    public void TryValidate_InventoryService()
    {
        // Arrange
        var req = new FaultInjectionRequest(
            TargetService: "InventoryAPI",
            FaultType: "HttpFailure",
            LatencyMs: null,
            StatusCode: 502);

        // Act
        var ok = ChaosRules.TryValidate(req, out var fault, out var error);

        // Assert
        Assert.IsTrue(ok);
        Assert.AreEqual("InventoryAPI", fault.TargetService);
    }

    [TestMethod]
    public void TryValidate_AuthService()
    {
        // Arrange
        var req = new FaultInjectionRequest(
            TargetService: "AuthService",
            FaultType: "Latency",
            LatencyMs: 200,
            Percent: 75);

        // Act
        var ok = ChaosRules.TryValidate(req, out var fault, out var error);

        // Assert
        Assert.IsTrue(ok);
        Assert.AreEqual("AuthService", fault.TargetService);
    }

    [TestMethod]
    public void TryValidate_ValidLatencyWithAllParams()
    {
        // Arrange — Latency uses LatencyMs + Percent + Duration; not StatusCode.
        var req = new FaultInjectionRequest(
            TargetService: "PaymentGateway",
            FaultType: "Latency",
            LatencyMs: 5000,
            DurationSeconds: 300,
            Percent: 25);

        // Act
        var ok = ChaosRules.TryValidate(req, out var fault, out var error);

        // Assert
        Assert.IsTrue(ok, error);
        Assert.AreEqual("PaymentGateway", fault.TargetService);
        Assert.AreEqual("Latency", fault.FaultType);
        Assert.AreEqual(5000, fault.LatencyMs);
        Assert.AreEqual(25, fault.Percent);
        Assert.IsNull(fault.StatusCode);
        Assert.AreEqual(TimeSpan.FromSeconds(300), fault.Duration);
    }

    [TestMethod]
    public void TryValidate_ValidHttpFailureWithAllParams()
    {
        // Arrange
        var req = new FaultInjectionRequest(
            TargetService: "AuthService",
            FaultType: "HttpFailure",
            LatencyMs: null,
            Percent: 100,
            StatusCode: 503);

        // Act
        var ok = ChaosRules.TryValidate(req, out var fault, out var error);

        // Assert
        Assert.IsTrue(ok);
        Assert.AreEqual("AuthService", fault.TargetService);
        Assert.AreEqual("HttpFailure", fault.FaultType);
        Assert.IsNull(fault.LatencyMs);
        Assert.AreEqual(100, fault.Percent);
        Assert.AreEqual(503, fault.StatusCode);
        Assert.AreEqual(TimeSpan.FromSeconds(300), fault.Duration);
    }

    [TestMethod]
    public void ValidatedFault_RecordShape()
    {
        // Arrange / Act - using positional constructor arguments matching the record declaration order
        // ValidatedFault record: TargetService, FaultType, LatencyMs, Percent, StatusCode, Duration
        var fault = new ValidatedFault(
            "PaymentGateway",
            "Latency",
            2500,       // LatencyMs
            42,         // Percent
            503,        // StatusCode
            TimeSpan.FromSeconds(120));  // Duration

        // Assert
        Assert.AreEqual("PaymentGateway", fault.TargetService);
        Assert.AreEqual("Latency", fault.FaultType);
        Assert.AreEqual(2500, fault.LatencyMs);
        Assert.AreEqual(42, fault.Percent);
        Assert.AreEqual(503, fault.StatusCode);
        Assert.AreEqual(TimeSpan.FromSeconds(120), fault.Duration);
    }
}