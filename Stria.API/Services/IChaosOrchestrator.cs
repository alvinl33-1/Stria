using Stria.Domain;
using Stria.DTOs;

namespace Stria.Services;

public interface IChaosOrchestrator
{
    /// <summary>
    /// Activates a fault for the request's target (replacing any active one), or clears the
    /// target when the fault type is "None". <paramref name="actor"/> must come from the server
    /// (the authenticated caller's validated claims), never from the request body.
    /// </summary>
    Task InjectFaultAsync(FaultInjectionRequest request, Actor actor,
        CancellationToken cancellationToken = default);
    Task<ChaosExperiment?> GetActiveFaultForServiceAsync(string serviceName,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChaosExperiment>> GetAllActiveFaultsAsync(CancellationToken cancellationToken = default);

    Task<int> ExpireDueFaultsAsync(CancellationToken cancellationToken = default);

    Task<KillSwitchState> GetKillSwitchAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears every active experiment and suppresses new injections until released. Injections
    /// while engaged throw <see cref="KillSwitchEngagedException"/>. Engaging when already
    /// engaged changes nothing and records no event. <paramref name="actor"/> comes from the server.
    /// </summary>
    Task<KillSwitchState> EngageKillSwitchAsync(string reason, Actor actor,
        CancellationToken cancellationToken = default);

    /// <summary>Allows injections again. Releasing when already released changes nothing.</summary>
    Task<KillSwitchState> ReleaseKillSwitchAsync(string reason, Actor actor,
        CancellationToken cancellationToken = default);
}
