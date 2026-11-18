using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Stria.Domain;
using Stria.DTOs;
using Stria.Services;
using Stria.Tests.TestSupport;

namespace Stria.Tests;

/// <summary>
/// Kill-switch behaviour of <see cref="DatabaseChaosOrchestrator"/> against real PostgreSQL. The
/// switch relies on an advisory lock and CHECK constraints, so an in-memory provider can't stand in.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class KillSwitchOrchestratorTests
{
    private static readonly Actor Admin = new("admin-oid", "Admin User");
    private static readonly Actor Operator = new("operator-oid", "Operator User");

    private static PostgresFixture _fixture = null!;
    private static bool _dockerAvailable;
    private static string? _dockerFailure;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
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

        await _fixture.ResetAsync();
    }

    private static DatabaseChaosOrchestrator CreateSut() =>
        new(_fixture.ContextFactory, TimeProvider.System, NullLogger<DatabaseChaosOrchestrator>.Instance);

    private static FaultInjectionRequest Latency(string service) =>
        new(TargetService: service, FaultType: "Latency", LatencyMs: 200);

    private static async Task<List<ChaosExperiment>> LoadExperimentsAsync()
    {
        await using var db = await _fixture.ContextFactory.CreateDbContextAsync();
        return await db.ChaosExperiments.AsNoTracking().ToListAsync();
    }

    private static async Task<List<KillSwitchEvent>> LoadEventsAsync()
    {
        await using var db = await _fixture.ContextFactory.CreateDbContextAsync();
        return await db.KillSwitchEvents.AsNoTracking().OrderBy(e => e.Id).ToListAsync();
    }

    [TestMethod]
    public async Task GetKillSwitch_NeverEngaged_ReportsReleased()
    {
        var state = await CreateSut().GetKillSwitchAsync();

        Assert.AreEqual(KillSwitchState.NeverEngaged, state);
    }

    [TestMethod]
    public async Task Engage_ClearsAllActiveExperiments_AsKillSwitch()
    {
        var sut = CreateSut();
        await sut.InjectFaultAsync(Latency("PaymentGateway"), Operator);
        await sut.InjectFaultAsync(Latency("AuthService"), Operator);

        var state = await sut.EngageKillSwitchAsync("  incident 42  ", Admin);

        Assert.IsTrue(state.Engaged);
        Assert.AreEqual("admin-oid", state.ChangedBy);
        Assert.AreEqual("Admin User", state.ChangedByName);
        Assert.AreEqual("incident 42", state.Reason);

        var experiments = await LoadExperimentsAsync();
        Assert.HasCount(2, experiments);
        Assert.IsTrue(experiments.All(e =>
            e.Status == ExperimentStatus.Cleared && e.ClearedBy == DatabaseChaosOrchestrator.KillSwitchActor));
        Assert.IsEmpty(await sut.GetAllActiveFaultsAsync());

        var change = (await LoadEventsAsync()).Single();
        Assert.IsTrue(change.Engaged);
        Assert.AreEqual(2, change.ClearedExperiments);
    }

    [TestMethod]
    public async Task Engage_LeaseThatAlreadyExpired_StaysAttributedToExpiry()
    {
        var now = DateTime.UtcNow;
        await using (var db = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            db.ChaosExperiments.Add(new ChaosExperiment
            {
                TargetService = "InventoryAPI",
                FaultType = "Latency",
                LatencyMs = 100,
                Status = ExperimentStatus.Active,
                TriggeredBy = "old-actor",
                CreatedAt = now.AddHours(-1),
                ExpiresAt = now.AddMinutes(-1)
            });
            await db.SaveChangesAsync();
        }

        await CreateSut().EngageKillSwitchAsync("incident", Admin);

        var expired = (await LoadExperimentsAsync()).Single();
        Assert.AreEqual(ExperimentStatus.Cleared, expired.Status);
        Assert.AreEqual(DatabaseChaosOrchestrator.ExpiryActor, expired.ClearedBy);
        Assert.AreEqual(0, (await LoadEventsAsync()).Single().ClearedExperiments);
    }

    [TestMethod]
    public async Task Inject_WhileEngaged_IsSuppressed()
    {
        var sut = CreateSut();
        await sut.EngageKillSwitchAsync("incident", Admin);

        await Assert.ThrowsExactlyAsync<KillSwitchEngagedException>(
            () => sut.InjectFaultAsync(Latency("PaymentGateway"), Operator));

        Assert.IsEmpty(await LoadExperimentsAsync());
        Assert.IsNull(await sut.GetActiveFaultForServiceAsync("PaymentGateway"));
    }

    [TestMethod]
    public async Task Clear_WhileEngaged_IsAllowed()
    {
        var sut = CreateSut();
        await sut.EngageKillSwitchAsync("incident", Admin);

        await sut.InjectFaultAsync(new FaultInjectionRequest("PaymentGateway", ChaosRules.NoFault), Operator);

        Assert.IsEmpty(await LoadExperimentsAsync());
    }

    [TestMethod]
    public async Task Release_AllowsInjectionAgain()
    {
        var sut = CreateSut();
        await sut.EngageKillSwitchAsync("incident", Admin);

        var state = await sut.ReleaseKillSwitchAsync("resolved", Admin);
        await sut.InjectFaultAsync(Latency("PaymentGateway"), Operator);

        Assert.IsFalse(state.Engaged);
        Assert.AreEqual("resolved", state.Reason);
        Assert.IsNotNull(await sut.GetActiveFaultForServiceAsync("PaymentGateway"));

        var history = await LoadEventsAsync();
        CollectionAssert.AreEqual(new[] { true, false }, history.Select(e => e.Engaged).ToArray());
        Assert.AreEqual(0, history[1].ClearedExperiments);
    }

    [TestMethod]
    public async Task EngageAndRelease_AreIdempotent()
    {
        var sut = CreateSut();

        var released = await sut.ReleaseKillSwitchAsync("nothing to release", Admin);
        var first = await sut.EngageKillSwitchAsync("first", Admin);
        var second = await sut.EngageKillSwitchAsync("second", Operator);

        Assert.AreEqual(KillSwitchState.NeverEngaged, released);
        Assert.AreEqual(first, second); // the repeat reports the existing engagement
        Assert.HasCount(1, await LoadEventsAsync());
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public async Task Engage_BlankReason_IsRejected(string reason)
    {
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => CreateSut().EngageKillSwitchAsync(reason, Admin));
        Assert.IsEmpty(await LoadEventsAsync());
    }

    [TestMethod]
    public async Task Engage_OverlongReason_IsRejected()
    {
        var reason = new string('x', KillSwitchEvent.MaxReasonLength + 1);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => CreateSut().EngageKillSwitchAsync(reason, Admin));
    }

    [TestMethod]
    public async Task Concurrency_InjectionsRacingEngage_NeverSurviveIt()
    {
        // Each injection either commits before the engage (and is cleared by it) or sees the
        // switch and is refused. None may end up Active once the engage has committed.
        var sut = CreateSut();

        for (var round = 0; round < 20; round++)
        {
            await sut.ReleaseKillSwitchAsync("next round", Admin);

            var injections = ChaosRules.Services
                .Select(service => InjectIgnoringSuppressionAsync(sut, service))
                .ToList();
            var engage = sut.EngageKillSwitchAsync($"round {round}", Admin);
            await Task.WhenAll(injections.Append(engage));

            Assert.IsEmpty(await sut.GetAllActiveFaultsAsync(), $"An injection survived engage in round {round}.");
        }
    }

    private static async Task InjectIgnoringSuppressionAsync(IChaosOrchestrator sut, string service)
    {
        try
        {
            await sut.InjectFaultAsync(Latency(service), Operator);
        }
        catch (KillSwitchEngagedException)
        {
        }
    }
}
