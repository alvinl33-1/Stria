using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Stria.Domain;
using Stria.DTOs;
using Stria.Endpoints;
using Stria.Services;

namespace Stria.Tests;

/// <summary>
/// The directive handler called directly, without a host or database. Authentication and rate
/// limits for this route are covered over HTTP in DataPlaneSecurityTests.
/// </summary>
[TestClass]
public sealed class DataPlaneEndpointsTests
{
    [TestMethod]
    public async Task UnknownService_Is404_WithoutQueryingTheOrchestrator()
    {
        var orchestrator = new StubOrchestrator(active: null);

        var response = await DataPlaneEndpoints.GetDirectiveAsync(
            "NoSuchService", orchestrator, TimeProvider.System, CancellationToken.None);

        Assert.IsTrue(response.Result is ProblemHttpResult { StatusCode: StatusCodes.Status404NotFound },
            $"Got {response.Result}");
        Assert.AreEqual(0, orchestrator.Queried.Count);
    }

    [TestMethod]
    public async Task KnownService_WithoutActiveFault_Is204()
    {
        var orchestrator = new StubOrchestrator(active: null);

        var response = await DataPlaneEndpoints.GetDirectiveAsync(
            "PaymentGateway", orchestrator, TimeProvider.System, CancellationToken.None);

        Assert.IsInstanceOfType<NoContent>(response.Result);
        CollectionAssert.AreEqual(new[] { "PaymentGateway" }, orchestrator.Queried);
    }

    /// <summary>Answers the one read the handler makes; anything else fails the test.</summary>
    private sealed class StubOrchestrator(ChaosExperiment? active) : IChaosOrchestrator
    {
        public List<string> Queried { get; } = [];

        public Task<ChaosExperiment?> GetActiveFaultForServiceAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            Queried.Add(serviceName);
            return Task.FromResult(active);
        }

        public Task InjectFaultAsync(FaultInjectionRequest request, Actor actor, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<IReadOnlyList<ChaosExperiment>> GetAllActiveFaultsAsync(CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<int> ExpireDueFaultsAsync(CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<KillSwitchState> GetKillSwitchAsync(CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<KillSwitchState> EngageKillSwitchAsync(string reason, Actor actor, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<KillSwitchState> ReleaseKillSwitchAsync(string reason, Actor actor, CancellationToken cancellationToken = default) => throw Unexpected();

        private static AssertFailedException Unexpected() => new("The directive handler must only read the active fault.");
    }
}
