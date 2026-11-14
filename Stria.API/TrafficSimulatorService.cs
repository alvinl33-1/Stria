using Stria.Domain;
using Stria.Services;

namespace Stria;

// Stria.API/TrafficSimulatorService.cs (Updated Snippet)
public class TrafficSimulatorService : BackgroundService
{
    private readonly IChaosOrchestrator _orchestrator;
    private readonly ILogger<TrafficSimulatorService> _logger;
    private readonly string[] _targetServices = { "PaymentGateway", "InventoryAPI", "AuthService" };

    public TrafficSimulatorService(IChaosOrchestrator orchestrator, ILogger<TrafficSimulatorService> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Stria.API Chaos Simulation Pipeline Booted using hardened Service Boundaries.");

        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var service in _targetServices)
            {
                var experiment = await _orchestrator.GetActiveFaultForServiceAsync(service);
                if (experiment is { Status: ExperimentStatus.Active })
                {
                    _logger.LogCritical("FAULT ENCOUNTERED on [{Service}]: Deployment Parameter -> {Fault}", service,
                        experiment.FaultType);
                }
                else
                {
                    _logger.LogInformation("Streaming routine packet transaction metadata for: {Service}...", service);
                }
            }

            await Task.Delay(2500, stoppingToken);
        }
    }
}