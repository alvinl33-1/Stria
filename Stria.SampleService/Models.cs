namespace Stria.SampleService;

public sealed class StriaOptions
{
    public const string SectionName = "Stria";

    /// <summary>Where the Stria host is reachable from this service, e.g. http://stria:8080.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Which registered target this instance plays: PaymentGateway, AuthService or InventoryAPI.</summary>
    public string ServiceName { get; set; } = string.Empty;

    public int PollIntervalMs { get; set; } = 1000;

    /// <summary>Cap on requests parked in an injected delay at once, so a fault can't exhaust the process.</summary>
    public int MaxConcurrentDelayed { get; set; } = 500;

    /// <summary>This service's data-plane key, sent as X-Stria-Service-Key. Never commit it.</summary>
    public string ServiceKey { get; set; } = string.Empty;
}

/// <summary>
/// This service's copy of Stria's directive contract. The two sides are coupled only by the JSON
/// shape, not by a shared assembly, so either can be built and deployed on its own.
/// </summary>
public sealed record FaultDirective(
    Guid ExperimentId,
    string FaultType,
    int? LatencyMs,
    int Percent,
    int? StatusCode,
    int RemainingSeconds);
