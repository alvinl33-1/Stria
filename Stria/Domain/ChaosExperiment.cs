namespace Stria.Domain;

public enum ExperimentStatus { Active, Cleared }

public class ChaosExperiment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TargetService { get; set; } = string.Empty;
    public string FaultType { get; set; } = "None";

    // Fault parameters. LatencyMs applies to Latency; StatusCode to HttpFailure;
    // Percent (share of requests affected) to Latency and HttpFailure.
    public int? LatencyMs { get; set; }
    public int Percent { get; set; } = 100;
    public int? StatusCode { get; set; }

    public ExperimentStatus Status { get; set; } = ExperimentStatus.Active;

    /// <summary>Who started it. Set by the server from the authenticated caller.</summary>
    public string TriggeredBy { get; set; } = "Manual";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The lease. A fault is only in effect until this instant, even if nobody clears it.
    /// Always set by the orchestrator (CreatedAt + requested duration).
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    public DateTime? ClearedAt { get; set; }

    /// <summary>Who ended it: the caller who cleared/replaced it, or "system:expiry".</summary>
    public string? ClearedBy { get; set; }
}