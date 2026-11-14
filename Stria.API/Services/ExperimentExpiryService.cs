using Microsoft.Extensions.Options;

namespace Stria.Services;

public sealed class ExperimentExpiryOptions
{
    public const string SectionName = "Expiry";

    public int SweepIntervalSeconds { get; set; } = 5;
}

/// <summary>
/// Marks experiments whose lease ran out as Cleared (attributed to "system:expiry").
/// Correctness does not depend on this service: readers already ignore expired leases.
/// This only keeps the stored Status truthful and frees the "active" slot in the index.
/// </summary>
public sealed class ExperimentExpiryService : BackgroundService
{
    private readonly IChaosOrchestrator _orchestrator;
    private readonly ExperimentExpiryOptions _options;
    private readonly ILogger<ExperimentExpiryService> _logger;

    public ExperimentExpiryService(
        IChaosOrchestrator orchestrator,
        IOptions<ExperimentExpiryOptions> options,
        ILogger<ExperimentExpiryService> logger)
    {
        _orchestrator = orchestrator;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, _options.SweepIntervalSeconds)));

        do
        {
            try
            {
                await _orchestrator.ExpireDueFaultsAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // An unhandled exception here would stop the whole host; log and retry next tick.
                _logger.LogError(ex, "Fault expiry sweep failed; will retry next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}