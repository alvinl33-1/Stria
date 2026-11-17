namespace Stria.SampleService;

/// <summary>
/// The fault this service is currently applying, held as a lease. The deadline is computed
/// locally from RemainingSeconds on a monotonic clock (TimeProvider.GetTimestamp), so:
///   * wall-clock skew or adjustments can't stretch or shorten a fault, and
///   * if Stria becomes unreachable the lease simply runs out by itself (fail-safe).
/// </summary>
public sealed class FaultState
{
    private sealed record Lease(FaultDirective Directive, long DeadlineTimestamp);

    private readonly TimeProvider _time;
    private volatile Lease? _current;

    public FaultState(TimeProvider time) => _time = time;

    /// <summary>Called after each successful poll. Null means "no active fault".</summary>
    public void Apply(FaultDirective? directive)
    {
        if (directive is null)
        {
            _current = null;
            return;
        }

        var ticks = (long)(directive.RemainingSeconds * (double)_time.TimestampFrequency);
        _current = new Lease(directive, _time.GetTimestamp() + ticks);
    }

    /// <summary>The directive to apply right now, or null if none or the lease has run out.</summary>
    public FaultDirective? GetActive()
    {
        var lease = _current;
        if (lease is null)
            return null;

        return _time.GetTimestamp() < lease.DeadlineTimestamp ? lease.Directive : null;
    }
}
