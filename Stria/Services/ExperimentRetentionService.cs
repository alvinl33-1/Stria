using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stria.Data;
using Stria.Domain;

namespace Stria.Services;

public sealed class ExperimentRetentionOptions
{
    public const string SectionName = "Retention";

    /// <summary>Delete Cleared experiments whose ClearedAt is older than this. 0 disables.</summary>
    public int MaxAgeDays { get; set; } = 90;

    /// <summary>Keep at most this many Cleared experiments (oldest deleted first). 0 disables.</summary>
    public int MaxClearedExperiments { get; set; } = 10_000;

    public int SweepIntervalMinutes { get; set; } = 360;
}

/// <summary>
/// Periodically trims experiment history. Only rows with Status = Cleared are ever deleted;
/// an Active experiment is never touched, however old it is.
/// </summary>
public sealed class ExperimentRetentionService : BackgroundService
{
    private readonly IDbContextFactory<StriaDbContext> _dbFactory;
    private readonly ExperimentRetentionOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<ExperimentRetentionService> _logger;

    public ExperimentRetentionService(
        IDbContextFactory<StriaDbContext> dbFactory,
        IOptions<ExperimentRetentionOptions> options,
        TimeProvider time,
        ILogger<ExperimentRetentionService> logger)
    {
        _dbFactory = dbFactory;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, _options.SweepIntervalMinutes)));

        do
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // An unhandled exception here would stop the whole host; log and try next interval.
                _logger.LogError(ex, "Experiment retention sweep failed; will retry next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var removed = 0;

        if (_options.MaxAgeDays > 0)
        {
            var cutoff = _time.GetUtcNow().UtcDateTime.AddDays(-_options.MaxAgeDays);
            removed += await db.ChaosExperiments
                .Where(e => e.Status == ExperimentStatus.Cleared && e.ClearedAt < cutoff)
                .ExecuteDeleteAsync(ct);
        }

        if (_options.MaxClearedExperiments > 0)
        {
            var cleared = db.ChaosExperiments.Where(e => e.Status == ExperimentStatus.Cleared);
            var excess = await cleared.CountAsync(ct) - _options.MaxClearedExperiments;
            if (excess > 0)
            {
                var oldestIds = cleared.OrderBy(e => e.ClearedAt).Take(excess).Select(e => e.Id);
                removed += await db.ChaosExperiments
                    .Where(e => e.Status == ExperimentStatus.Cleared && oldestIds.Contains(e.Id))
                    .ExecuteDeleteAsync(ct);
            }
        }

        if (removed > 0)
            _logger.LogInformation("Retention sweep removed {Count} cleared experiment(s).", removed);
    }
}