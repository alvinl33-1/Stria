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
    public const string KillSwitchActor = "system:kill-switch";

    // PostgreSQL advisory lock serialising kill-switch changes against injections. Injections take
    // it shared (they don't block each other); engage/release take it exclusive. Arbitrary but fixed.
    private const long KillSwitchLockKey = 0x5374_7269_614B_5357; // "StriaKSW"

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
        Actor actor,
        CancellationToken cancellationToken = default)
    {
        // Re-check invariants here too: the API boundary is not the only possible caller.
        if (!ChaosRules.TryValidate(request, out var fault, out var error))
            throw new ArgumentException(error, nameof(request));

        actor = ValidateActor(actor);

        using var activity = StriaTelemetry.Source.StartActivity("stria.fault.inject");
        activity?.SetTag(StriaTelemetry.TargetServiceTag, fault.TargetService);
        activity?.SetTag(StriaTelemetry.FaultTypeTag, fault.FaultType);

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

        using var activity = StriaTelemetry.Source.StartActivity("stria.directive.read");
        activity?.SetTag(StriaTelemetry.TargetServiceTag, service);

        var now = _time.GetUtcNow().UtcDateTime;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var active = await db.ChaosExperiments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                e => e.TargetService == service && e.Status == ExperimentStatus.Active && e.ExpiresAt > now,
                cancellationToken);

        activity?.SetTag(StriaTelemetry.DirectiveFoundTag, active is not null);
        return active;
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

    public async Task<KillSwitchState> GetKillSwitchAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return KillSwitchState.From(await LatestKillSwitchEventAsync(db, cancellationToken));
    }

    public Task<KillSwitchState> EngageKillSwitchAsync(
        string reason,
        Actor actor,
        CancellationToken cancellationToken = default) =>
        SetKillSwitchAsync(engage: true, reason, actor, cancellationToken);

    public Task<KillSwitchState> ReleaseKillSwitchAsync(
        string reason,
        Actor actor,
        CancellationToken cancellationToken = default) =>
        SetKillSwitchAsync(engage: false, reason, actor, cancellationToken);

    private async Task<KillSwitchState> SetKillSwitchAsync(bool engage, string reason, Actor actor, CancellationToken ct)
    {
        actor = ValidateActor(actor);
        if (!KillSwitchEvent.TryNormalizeReason(reason, out var normalizedReason, out var error))
            throw new ArgumentException(error, nameof(reason));

        using var activity = StriaTelemetry.Source.StartActivity(
            engage ? "stria.kill_switch.engage" : "stria.kill_switch.release");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Exclusive: waits for in-flight injections to commit, and any injection that starts
        // after this point reads the new state once we commit.
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({KillSwitchLockKey})", ct);

        var latest = await LatestKillSwitchEventAsync(db, ct);
        if ((latest?.Engaged ?? false) == engage)
        {
            await tx.CommitAsync(ct);
            return KillSwitchState.From(latest);
        }

        // PostgreSQL stores microseconds; truncate so the state we return equals what is persisted
        // (Linux clocks carry 100 ns ticks, which would otherwise differ after a read-back).
        var utcNow = _time.GetUtcNow().UtcDateTime;
        DateTime now = utcNow.AddTicks(-(utcNow.Ticks % TimeSpan.TicksPerMicrosecond));
        DateTime? clearedAt = now;
        var cleared = 0;

        if (engage)
        {
            // Leases that already ran out ended by themselves; only the rest are the switch's doing.
            await ReapExpiredAsync(db, now, null, ct);
            cleared = await db.ChaosExperiments
                .Where(e => e.Status == ExperimentStatus.Active)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.Status, ExperimentStatus.Cleared)
                    .SetProperty(e => e.ClearedAt, clearedAt)
                    .SetProperty(e => e.ClearedBy, KillSwitchActor)
                    .SetProperty(e => e.ClearedByName, (string?)null), ct);
        }

        var change = new KillSwitchEvent
        {
            Engaged = engage,
            Actor = actor.Id,
            ActorName = actor.DisplayName,
            Reason = normalizedReason,
            OccurredAt = now,
            ClearedExperiments = cleared
        };
        db.KillSwitchEvents.Add(change);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogWarning(
            "Kill switch {Action} by {Actor}; {Cleared} active experiment(s) cleared.",
            engage ? "engaged" : "released", actor.Id, cleared);

        return KillSwitchState.From(change);
    }

    private async Task ApplyAsync(ValidatedFault fault, Actor actor, CancellationToken ct)
    {
        var target = fault.TargetService;
        DateTime now = _time.GetUtcNow().UtcDateTime;
        DateTime? clearedAt = now;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // 0. Shared lock, then read the switch: an engage either finished before this read (we see
        //    it) or waits for this transaction to commit (and then clears what we wrote).
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock_shared({KillSwitchLockKey})", ct);
        if (fault.FaultType != ChaosRules.NoFault && (await LatestKillSwitchEventAsync(db, ct))?.Engaged == true)
            throw new KillSwitchEngagedException();

        // 1. A lease that already ran out ended by itself; don't attribute it to this caller.
        await ReapExpiredAsync(db, now, target, ct);

        // 2. Anything still active is being cleared or replaced by this caller.
        var cleared = await db.ChaosExperiments
            .Where(e => e.TargetService == target && e.Status == ExperimentStatus.Active)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, ExperimentStatus.Cleared)
                .SetProperty(e => e.ClearedAt, clearedAt)
                .SetProperty(e => e.ClearedBy, actor.Id)
                .SetProperty(e => e.ClearedByName, actor.DisplayName), ct);

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
                TriggeredBy = actor.Id,
                TriggeredByName = actor.DisplayName,
                CreatedAt = now,
                ExpiresAt = now + fault.Duration
            });
            await db.SaveChangesAsync(ct);
        }

        await tx.CommitAsync(ct);

        _logger.LogInformation(
            "{FaultType} on {Service} applied by {Actor} for {Duration}; {Cleared} previous experiment(s) cleared.",
            fault.FaultType, target, actor.Id, fault.Duration, cleared);
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

    private static Task<KillSwitchEvent?> LatestKillSwitchEventAsync(StriaDbContext db, CancellationToken ct) =>
        db.KillSwitchEvents.AsNoTracking().OrderByDescending(e => e.Id).FirstOrDefaultAsync(ct);

    private static Actor ValidateActor(Actor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var actorId = actor.Id?.Trim();
        if (string.IsNullOrEmpty(actorId) || actorId.Length > ChaosRules.MaxTriggeredByLength)
            throw new ArgumentException("Invalid actor identity.", nameof(actor));
        if (actor.DisplayName is { Length: > Actor.MaxDisplayNameLength })
            throw new ArgumentException("Actor display name is too long.", nameof(actor));
        return actor with { Id = actorId };
    }

    private static bool IsActiveTargetConflict(DbUpdateException ex) =>
        ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: StriaDbContext.ActiveTargetIndexName
        };
}
