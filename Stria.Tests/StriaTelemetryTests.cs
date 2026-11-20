using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelemetry.Trace;
using Stria.Domain;
using Stria.DTOs;
using Stria.Services;
using Stria.Tests.TestSupport;

namespace Stria.Tests;

/// <summary>
/// The orchestrator's spans against real PostgreSQL. Listeners are process-wide and test classes
/// run in parallel, so only spans under this test's own root activity are counted.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class StriaTelemetryTests
{
    private static PostgresFixture _postgres = null!;
    private static string? _failure;
    private static bool _ready;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _postgres = new PostgresFixture();
        _ready = DockerRequirement.TryStart(_postgres, out _failure);
    }

    [ClassCleanup]
    public static void ClassCleanup() => _postgres?.DisposeAsync().AsTask().GetAwaiter().GetResult();

    [TestMethod]
    public async Task Orchestrator_EmitsSpans_TaggedWithServiceAndFaultOnly()
    {
        if (!_ready)
            DockerRequirement.SkipIfUnavailable(_failure);
        await _postgres.ResetAsync();

        var stopped = new ConcurrentBag<Activity>();
        using var testSource = new ActivitySource("Stria.Tests.Telemetry");
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is StriaTelemetry.SourceName or "Stria.Tests.Telemetry",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Add
        };
        ActivitySource.AddActivityListener(listener);

        var actor = new Actor("telemetry-actor-oid", "Telemetry Actor");
        var sut = new DatabaseChaosOrchestrator(
            _postgres.ContextFactory, TimeProvider.System, NullLogger<DatabaseChaosOrchestrator>.Instance);

        ActivityTraceId traceId;
        using (var root = testSource.StartActivity("test-root")!)
        {
            traceId = root.TraceId;
            await sut.InjectFaultAsync(new FaultInjectionRequest("PaymentGateway", "Latency", LatencyMs: 500), actor);
            await sut.GetActiveFaultForServiceAsync("PaymentGateway");
            await sut.EngageKillSwitchAsync("telemetry test reason", actor);
            await sut.ReleaseKillSwitchAsync("telemetry test reason", actor);
        }

        var spans = stopped
            .Where(a => a.TraceId == traceId && a.Source.Name == StriaTelemetry.SourceName)
            .ToList();
        Activity Span(string name) => spans.Single(a => a.OperationName == name);

        var inject = Span("stria.fault.inject");
        Assert.AreEqual("PaymentGateway", inject.GetTagItem(StriaTelemetry.TargetServiceTag));
        Assert.AreEqual("Latency", inject.GetTagItem(StriaTelemetry.FaultTypeTag));

        var read = Span("stria.directive.read");
        Assert.AreEqual("PaymentGateway", read.GetTagItem(StriaTelemetry.TargetServiceTag));
        Assert.AreEqual(true, read.GetTagItem(StriaTelemetry.DirectiveFoundTag));

        Span("stria.kill_switch.engage");
        Span("stria.kill_switch.release");

        // Who did it and why belong in the audit table, not in exported telemetry.
        var tagValues = spans.SelectMany(a => a.TagObjects).Select(t => t.Value?.ToString() ?? "").ToList();
        Assert.IsFalse(tagValues.Any(v => v.Contains("telemetry-actor-oid") || v.Contains("Telemetry Actor")));
        Assert.IsFalse(tagValues.Any(v => v.Contains("telemetry test reason")));
    }

    [TestMethod]
    public void NoConnectionString_RegistersNoExporter()
    {
        var services = new ServiceCollection();

        Assert.IsFalse(services.AddStriaTelemetry(new ConfigurationBuilder().Build()));
        Assert.IsFalse(services.Any(d => d.ServiceType == typeof(TracerProvider)));
    }

    [TestMethod]
    public void ConnectionString_RegistersTracing()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [StriaTelemetry.ConnectionStringKey] = "InstrumentationKey=00000000-0000-0000-0000-000000000000"
            })
            .Build();

        Assert.IsTrue(services.AddStriaTelemetry(configuration));
        Assert.IsTrue(services.Any(d => d.ServiceType == typeof(TracerProvider)));
    }
}
