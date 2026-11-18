namespace Stria.Domain;

/// <summary>
/// One engage or release of the global kill switch. Rows are append-only: the latest row is the
/// current state (no rows means released), and the full table is the audit history.
/// </summary>
public class KillSwitchEvent
{
    public const int MaxReasonLength = 500;

    public long Id { get; set; }

    /// <summary>True for an engage, false for a release.</summary>
    public bool Engaged { get; set; }

    /// <summary>Who changed it. Set by the server from the authenticated caller.</summary>
    public string Actor { get; set; } = string.Empty;

    /// <summary>Display name of <see cref="Actor"/> at the time, for people reading the audit trail.</summary>
    public string? ActorName { get; set; }

    public string Reason { get; set; } = string.Empty;

    public DateTime OccurredAt { get; set; }

    /// <summary>Active experiments this engage cleared; always 0 for a release.</summary>
    public int ClearedExperiments { get; set; }

    /// <summary>A reason is required and bounded so the audit trail always says why.</summary>
    public static bool TryNormalizeReason(string? reason, out string normalized, out string error)
    {
        normalized = reason?.Trim() ?? string.Empty;
        error = normalized.Length switch
        {
            0 => "A reason is required.",
            > MaxReasonLength => $"Reason must be at most {MaxReasonLength} characters.",
            _ => string.Empty
        };
        return error.Length == 0;
    }
}

/// <summary>Current kill-switch state as reported to callers. <see cref="ChangedAt"/> is null if it was never engaged.</summary>
public sealed record KillSwitchState(
    bool Engaged,
    DateTime? ChangedAt,
    string? ChangedBy,
    string? ChangedByName,
    string? Reason)
{
    public static readonly KillSwitchState NeverEngaged = new(false, null, null, null, null);

    public static KillSwitchState From(KillSwitchEvent? latest) => latest is null
        ? NeverEngaged
        : new(latest.Engaged, latest.OccurredAt, latest.Actor, latest.ActorName, latest.Reason);
}

/// <summary>Thrown when a fault injection is attempted while the kill switch is engaged.</summary>
public sealed class KillSwitchEngagedException()
    : InvalidOperationException("Fault injection is suspended by the kill switch.");
