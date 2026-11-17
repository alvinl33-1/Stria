using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Stria.Data;
using Stria.Domain;
using Stria.DTOs;
using Stria.Services;
using Stria.Tests.TestSupport;

namespace Stria.Tests;

/// <summary>
/// Integration tests for <see cref="DatabaseChaosOrchestrator"/> against real PostgreSQL.
/// We need the real provider because the schema uses a partial unique index, CHECK constraints,
/// and <c>ExecuteUpdateAsync</c> — none of which an in-memory provider reproduces faithfully.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class DatabaseChaosOrchestratorTests
{
    private static PostgresFixture _fixture = null!;
    private static bool _dockerAvailable;
    private static string? _dockerFailure;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        // One container for the whole class: starting Postgres per method is ~7s each.
        // Assembly parallelization is ClassLevel so classes don't share this fixture.
        _fixture = new PostgresFixture();
        _dockerAvailable = DockerRequirement.TryStart(_fixture, out _dockerFailure);
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        if (_fixture is not null)
            await _fixture.DisposeAsync();
    }

    [TestInitialize]
    public async Task TestInit()
    {
        if (!_dockerAvailable)
            Assert.Inconclusive(_dockerFailure ?? "Docker/PostgreSQL test container unavailable.");

        // Wipe rows so each test starts from an empty ChaosExperiments table.
        await _fixture.ResetAsync();
    }

    private static DatabaseChaosOrchestrator CreateSut() =>
        new(
            _fixture.ContextFactory,
            TimeProvider.System,
            LoggerFactory.Create(b => b.AddConsole()).CreateLogger<DatabaseChaosOrchestrator>());

    /// <summary>
    /// Always read through a fresh context. The orchestrator writes with its own DbContext;
    /// reusing a tracked context after ExecuteUpdateAsync can show stale Active entities.
    /// </summary>
    private static async Task<List<ChaosExperiment>> LoadForServiceAsync(string service)
    {
        await using var db = await _fixture.ContextFactory.CreateDbContextAsync();
        return await db.ChaosExperiments
            .AsNoTracking()
            .Where(e => e.TargetService == service)
            .ToListAsync();
    }

    private static async Task SeedActiveAsync(ChaosExperiment experiment)
    {
        await using var db = await _fixture.ContextFactory.CreateDbContextAsync();
        db.ChaosExperiments.Add(experiment);
        await db.SaveChangesAsync();
    }

    [TestMethod]
    public async Task InjectFault_CreatesActiveExperiment()
    {
        var sut = CreateSut();

        await sut.InjectFaultAsync(
            new FaultInjectionRequest(
                TargetService: "PaymentGateway",
                FaultType: "Latency",
                LatencyMs: 750),
            "test-actor");

        var all = await LoadForServiceAsync("PaymentGateway");
        var active = all.SingleOrDefault(e => e.Status == ExperimentStatus.Active);

        Assert.IsNotNull(active);
        Assert.AreEqual("Latency", active.FaultType);
        Assert.AreEqual(750, active.LatencyMs);
        Assert.AreEqual(100, active.Percent);
        Assert.IsNull(active.StatusCode); // StatusCode is HttpFailure-only
        Assert.AreEqual("test-actor", active.TriggeredBy);
        Assert.IsTrue(active.ExpiresAt > active.CreatedAt);
    }

    [TestMethod]
    public async Task InjectFault_ReplacesPriorActive_ForSameTarget()
    {
        await SeedActiveAsync(new ChaosExperiment
        {
            TargetService = "AuthService",
            FaultType = "HttpFailure",
            LatencyMs = null,
            Percent = 100,
            StatusCode = 503,
            Status = ExperimentStatus.Active,
            TriggeredBy = "old-actor",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(2)
        });

        var sut = CreateSut();
        await sut.InjectFaultAsync(
            new FaultInjectionRequest(
                TargetService: "AuthService",
                FaultType: "Latency",
                LatencyMs: 100),
            "new-actor");

        var all = await LoadForServiceAsync("AuthService");
        var active = all.SingleOrDefault(e => e.Status == ExperimentStatus.Active);
        Assert.IsNotNull(active);
        Assert.AreEqual(100, active.LatencyMs);
        Assert.AreEqual("new-actor", active.TriggeredBy);

        var cleared = all.SingleOrDefault(e => e.Status == ExperimentStatus.Cleared);
        Assert.IsNotNull(cleared);
        Assert.IsNotNull(cleared.ClearedAt);
        // The actor who replaced the fault is recorded as ClearedBy.
        Assert.AreEqual("new-actor", cleared.ClearedBy);
    }

    [TestMethod]
    public async Task InjectFault_WithNone_ClearsActiveAndCreatesNoRow()
    {
        await SeedActiveAsync(new ChaosExperiment
        {
            TargetService = "PaymentGateway",
            FaultType = "Latency",
            LatencyMs = 100,
            Percent = 50,
            StatusCode = null,
            Status = ExperimentStatus.Active,
            TriggeredBy = "before-clear",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(2)
        });

        var sut = CreateSut();
        await sut.InjectFaultAsync(
            new FaultInjectionRequest(
                TargetService: "PaymentGateway",
                FaultType: "None"),
            "clearer");

        var all = await LoadForServiceAsync("PaymentGateway");
        Assert.IsNull(all.SingleOrDefault(e => e.Status == ExperimentStatus.Active));

        var cleared = all.SingleOrDefault(e => e.Status == ExperimentStatus.Cleared);
        Assert.IsNotNull(cleared);
        Assert.IsNotNull(cleared.ClearedAt);
        Assert.AreEqual("clearer", cleared.ClearedBy);
    }

    [TestMethod]
    public async Task GetActiveFaultForService_UnknownService_ReturnsNull()
    {
        var result = await CreateSut().GetActiveFaultForServiceAsync("GhostService");
        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task GetActiveFaultForService_Expired_ReturnsNull()
    {
        var now = DateTime.UtcNow;
        await SeedActiveAsync(new ChaosExperiment
        {
            TargetService = "PaymentGateway",
            FaultType = "Latency",
            LatencyMs = 100,
            Percent = 50,
            StatusCode = null,
            Status = ExperimentStatus.Active,
            TriggeredBy = "old-actor",
            CreatedAt = now.AddHours(-1),
            ExpiresAt = now.AddMinutes(-1)
        });

        var result = await CreateSut().GetActiveFaultForServiceAsync("PaymentGateway");
        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task GetAllActiveFaults_ExcludesExpired()
    {
        var now = DateTime.UtcNow;
        await SeedActiveAsync(new ChaosExperiment
        {
            TargetService = "PaymentGateway",
            FaultType = "Latency",
            LatencyMs = 100,
            Percent = 50,
            StatusCode = null,
            Status = ExperimentStatus.Active,
            TriggeredBy = "actor-a",
            CreatedAt = now.AddHours(-1),
            ExpiresAt = now.AddMinutes(-1)
        });
        await SeedActiveAsync(new ChaosExperiment
        {
            TargetService = "AuthService",
            FaultType = "HttpFailure",
            LatencyMs = null,
            Percent = 100,
            StatusCode = 503,
            Status = ExperimentStatus.Active,
            TriggeredBy = "actor-b",
            CreatedAt = now,
            ExpiresAt = now.AddHours(2)
        });

        var list = (await CreateSut().GetAllActiveFaultsAsync()).ToList();
        Assert.HasCount(1, list);
        Assert.AreEqual("AuthService", list[0].TargetService);
    }

    [TestMethod]
    public async Task ExpireDueFaults_ClearsExpiredAndReturnsCount()
    {
        var now = DateTime.UtcNow;
        await SeedActiveAsync(new ChaosExperiment
        {
            TargetService = "PaymentGateway",
            FaultType = "Latency",
            LatencyMs = 100,
            Percent = 50,
            StatusCode = null,
            Status = ExperimentStatus.Active,
            TriggeredBy = "actor-a",
            CreatedAt = now.AddHours(-1),
            ExpiresAt = now.AddMinutes(-1)
        });

        var count = await CreateSut().ExpireDueFaultsAsync();
        Assert.AreEqual(1, count);

        var all = await LoadForServiceAsync("PaymentGateway");
        Assert.IsNull(all.SingleOrDefault(e => e.Status == ExperimentStatus.Active));

        var cleared = all.SingleOrDefault(e => e.Status == ExperimentStatus.Cleared);
        Assert.IsNotNull(cleared);
        Assert.AreEqual(DatabaseChaosOrchestrator.ExpiryActor, cleared.ClearedBy);
        Assert.IsNotNull(cleared.ClearedAt);
    }

    [TestMethod]
    public async Task ExpireDueFaults_NoExpiry_ReturnsZero()
    {
        await SeedActiveAsync(new ChaosExperiment
        {
            TargetService = "PaymentGateway",
            FaultType = "Latency",
            LatencyMs = 100,
            Percent = 50,
            StatusCode = null,
            Status = ExperimentStatus.Active,
            TriggeredBy = "actor-a",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(2)
        });

        var count = await CreateSut().ExpireDueFaultsAsync();
        Assert.AreEqual(0, count);
    }

    [TestMethod]
    public async Task Concurrency_TwoInjections_LeavesOneActive()
    {
        // Exercises the partial unique index + retry path (last-writer-wins).
        var sut1 = CreateSut();
        var sut2 = CreateSut();

        await Task.WhenAll(
            sut1.InjectFaultAsync(
                new FaultInjectionRequest(
                    TargetService: "PaymentGateway",
                    FaultType: "Latency",
                    LatencyMs: 300),
                "actor-1"),
            sut2.InjectFaultAsync(
                new FaultInjectionRequest(
                    TargetService: "PaymentGateway",
                    FaultType: "Latency",
                    LatencyMs: 500),
                "actor-2"));

        var all = await LoadForServiceAsync("PaymentGateway");
        var active = all.Where(e => e.Status == ExperimentStatus.Active).ToList();

        // The safety invariant is "exactly one Active" — not which writer finished last.
        Assert.HasCount(1, active);
        Assert.IsTrue(active[0].LatencyMs is 300 or 500);
    }
}
