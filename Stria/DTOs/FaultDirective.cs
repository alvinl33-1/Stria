namespace Stria.DTOs;

/// <summary>
/// What a data-plane service (e.g. the sample service) needs to apply a fault. It deliberately
/// exposes the lease as RemainingSeconds, a duration, rather than an absolute ExpiresAt
/// timestamp: the consumer measures it on its own monotonic clock, so clock skew between
/// machines cannot stretch or shorten a fault.
/// </summary>
public record FaultDirective(
    Guid ExperimentId, 
    string FaultType,
    int? LatencyMs,
    int Percent, 
    int? StatusCode,
    int RemainingSeconds);