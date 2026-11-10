using Microsoft.EntityFrameworkCore;
using Npgsql;
using Stria.Data;
using Stria.Domain;
using Stria.DTOs;

namespace Stria.Services;

/// <summary>
/// Persists every experiment as a row. "Active" means Status = Active AND the lease
/// (ExpiresAt) has not run out. Readers enforce the lease themselves, so an expired fault
/// stops being reported immediately even if the background reaper is behind or down; the
/// reaper then tidies the Status column. Registered as a singleton, so it creates a
/// short-lived DbContext per operation from the factory.
/// </summary>
public class DatabaseChaosOrchestrator : IChaosOrchestrator
{
    public const string ExpiryActor = "system:expiry";

    private const int MaxAttempts = 3;

    private readonly IDbContextFactory<StriaDbContext> _dbFactory;
    private readonly TimeProvider _time;
    private readonly ILogger<DatabaseChaosOrchestrator> _logger;

    public DatabaseChaosOrchestrator(
        IDbContextFactory<StriaDbContext> dbFactory,
        TimeProvider time,
        ILogger<DatabaseChaosOrchestrator> logger)
    {
        _dbFactory = dbFactory;
        _time = time;
        _logger = logger;
    }

    public async Task InjectFaultAsync(
        FaultInjectionRequest request,
        string triggeredBy,
        CancellationToken cancellationToken = default)
    {
        // Re-check invariants here too: the API boundary is not the only possible caller.
        if (!ChaosRules.TryValidate(request, out var fault, out var error))
            throw new ArgumentException(error, nameof(request));

        var actor = triggeredBy?.Trim();
        if (string.IsNullOrEmpty(actor) || actor.Length > ChaosRules.MaxTriggeredByLength)
            throw new ArgumentException("Invalid actor identity.", nameof(triggeredBy));

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await ApplyAsync(fault, actor, cancellationToken);
                return;
            }
            catch (DbUpdateException ex) when (attempt < MaxAttempts && IsActiveTargetConflict(ex))
            {
                // Two injections for the same target raced; the partial unique index rejected
                // the loser. Retrying re-clears the winner's row, i.e. last writer wins.
                _logger.LogDebug(
                    "Concurrent activation on {Service} (attempt {Attempt}/{Max}); retrying.",
                    fault.TargetService, attempt, MaxAttempts);
            }
        }
    }

    public async Task<ChaosExperiment?> GetActiveFaultForServiceAsync(
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        if (!ChaosRules.TryGetService(serviceName, out var service))
            return null;

        var now = _time.GetUtcNow().UtcDateTime;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.ChaosExperiments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                e => e.TargetService == service && e.Status == ExperimentStatus.Active && e.ExpiresAt > now,
                cancellationToken);
    }

    public async Task<IReadOnlyList<ChaosExperiment>> GetAllActiveFaultsAsync(
        CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow().UtcDateTime;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.ChaosExperiments
            .AsNoTracking()
            .Where(e => e.Status == ExperimentStatus.Active && e.ExpiresAt > now)
            .OrderBy(e => e.TargetService)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> ExpireDueFaultsAsync(CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow().UtcDateTime;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var count = await ReapExpiredAsync(db, now, null, cancellationToken);

        if (count > 0)
            _logger.LogInformation("{Count} fault lease(s) expired and were cleared.", count);

        return count;
    }

    private async Task ApplyAsync(ValidatedFault fault, string actor, CancellationToken ct)
    {
        var target = fault.TargetService;
        DateTime now = _time.GetUtcNow().UtcDateTime;
        DateTime? clearedAt = now;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // 1. A lease that already ran out ended by itself; don't attribute it to this caller.
        await ReapExpiredAsync(db, now, target, ct);

        // 2. Anything still active is being cleared or replaced by this caller.
        var cleared = await db.ChaosExperiments
            .Where(e => e.TargetService == target && e.Status == ExperimentStatus.Active)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, ExperimentStatus.Cleared)
                .SetProperty(e => e.ClearedAt, clearedAt)
                .SetProperty(e => e.ClearedBy, actor), ct);

        // 3. "None" only clears; every other fault type opens a new Active row with a lease.
        if (fault.FaultType != ChaosRules.NoFault)
        {
            db.ChaosExperiments.Add(new ChaosExperiment
            {
                TargetService = target,
                FaultType = fault.FaultType,
                LatencyMs = fault.LatencyMs,
                Percent = fault.Percent,
                StatusCode = fault.StatusCode,
                Status = ExperimentStatus.Active,
                TriggeredBy = actor,
                CreatedAt = now,
                ExpiresAt = now + fault.Duration
            });
            await db.SaveChangesAsync(ct);
        }

        await tx.CommitAsync(ct);

        _logger.LogInformation(
            "{FaultType} on {Service} applied by {Actor} for {Duration}; {Cleared} previous experiment(s) cleared.",
            fault.FaultType, target, actor, fault.Duration, cleared);
    }

    /// <summary>
    /// Clears Active rows whose lease has run out. ClearedAt is the moment the lease actually
    /// ended (ExpiresAt), not the moment the reaper noticed, so the audit trail stays accurate
    /// even if the reaper was down for a while.
    /// </summary>
    private static Task<int> ReapExpiredAsync(StriaDbContext db, DateTime now, string? target, CancellationToken ct)
    {
        var query = db.ChaosExperiments
            .Where(e => e.Status == ExperimentStatus.Active && e.ExpiresAt <= now);

        if (target is not null)
            query = query.Where(e => e.TargetService == target);

        return query.ExecuteUpdateAsync(s => s
            .SetProperty(e => e.Status, ExperimentStatus.Cleared)
            .SetProperty(e => e.ClearedAt, e => (DateTime?)e.ExpiresAt)
            .SetProperty(e => e.ClearedBy, ExpiryActor), ct);
    }

    private static bool IsActiveTargetConflict(DbUpdateException ex) =>
        ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: StriaDbContext.ActiveTargetIndexName
        };
}