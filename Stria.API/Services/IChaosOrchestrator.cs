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
}
