using Stria.DTOs;

namespace Stria.Domain;

/// <summary>A request that passed validation, with canonical casing and defaults applied.</summary>
public readonly record struct ValidatedFault(
    string TargetService,
    string FaultType,
    int? LatencyMs,
    int Percent,
    int? StatusCode,
    TimeSpan Duration);

/// <summary>
/// Single source of truth for registered targets, fault types and parameter bounds.
/// The API boundary uses it to reject bad input; the orchestrator re-checks it so the
/// invariants hold no matter who calls it.
/// </summary>
public static class ChaosRules
{
    public const string Latency = "Latency";
    public const string HttpFailure = "HttpFailure";
    public const string CpuSpike = "CpuSpike";
    public const string NoFault = "None";

    public const int MinLatencyMs = 1;
    public const int MaxLatencyMs = 60_000;

    // Every fault is a lease. Without an explicit duration it gets the default.
    public const int DefaultDurationSeconds = 300;
    public const int MinDurationSeconds = 1;
    public const int MaxDurationSeconds = 3_600;

    public const int DefaultPercent = 100;
    public const int MinPercent = 1;
    public const int MaxPercent = 100;

    public const int DefaultStatusCode = 503;
    public const int MinStatusCode = 500;
    public const int MaxStatusCode = 599;

    /// <summary>Max length for TriggeredBy / ClearedBy.</summary>
    public const int MaxTriggeredByLength = 256;

    public static IReadOnlyList<string> Services { get; } =
        new[] { "PaymentGateway", "AuthService", "InventoryAPI" };

    // Static initializers run in textual order: Services must stay above ServiceMap.
    private static readonly Dictionary<string, string> ServiceMap =
        Services.ToDictionary(s => s, s => s, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> FaultTypeMap =
        new[] { Latency, HttpFailure, CpuSpike, NoFault }
            .ToDictionary(f => f, f => f, StringComparer.OrdinalIgnoreCase);

    /// <summary>Maps any casing of a registered service name to its canonical form.</summary>
    public static bool TryGetService(string? name, out string canonical)
    {
        if (name is not null && ServiceMap.TryGetValue(name, out var found))
        {
            canonical = found;
            return true;
        }

        canonical = string.Empty;
        return false;
    }

    public static bool TryValidate(FaultInjectionRequest? request, out ValidatedFault fault, out string error)
    {
        fault = default;
        error = string.Empty;

        if (request is null)
        {
            error = "Request body is required.";
            return false;
        }

        if (!TryGetService(request.TargetService, out var service))
        {
            error = "Unknown target service.";
            return false;
        }

        if (request.FaultType is null || !FaultTypeMap.TryGetValue(request.FaultType, out var faultType))
        {
            error = "Unsupported fault type.";
            return false;
        }

        // Latency is REQUIRED (and bounded) for Latency faults, and REJECTED for every other type.
        // A null LatencyMs does not match the range pattern, so it is invalid for Latency.
        var latencyValid = faultType == Latency
            ? request.LatencyMs is >= MinLatencyMs and <= MaxLatencyMs
            : request.LatencyMs is null;

        if (!latencyValid)
        {
            error = "Latency must be between 1 and 60000 ms for latency faults only.";
            return false;
        }

        // Clearing takes no parameters: reject them rather than silently ignoring them.
        if (faultType == NoFault)
        {
            if (request.DurationSeconds is not null || request.Percent is not null || request.StatusCode is not null)
            {
                error = "Clearing a fault (None) takes no other parameters.";
                return false;
            }

            fault = new ValidatedFault(service, faultType, null, DefaultPercent, null, TimeSpan.Zero);
            return true;
        }

        var durationSeconds = request.DurationSeconds ?? DefaultDurationSeconds;
        if (durationSeconds is < MinDurationSeconds or > MaxDurationSeconds)
        {
            error = "DurationSeconds must be between 1 and 3600.";
            return false;
        }

        // Percent = share of requests affected; meaningful for Latency and HttpFailure only.
        if (request.Percent is not null && faultType is not (Latency or HttpFailure))
        {
            error = "Percent applies to Latency and HttpFailure faults only.";
            return false;
        }

        var percent = request.Percent ?? DefaultPercent;
        if (percent is < MinPercent or > MaxPercent)
        {
            error = "Percent must be between 1 and 100.";
            return false;
        }

        if (request.StatusCode is not null && faultType != HttpFailure)
        {
            error = "StatusCode applies to HttpFailure faults only.";
            return false;
        }

        int? statusCode = faultType == HttpFailure ? (request.StatusCode ?? DefaultStatusCode) : null;
        if (statusCode is < MinStatusCode or > MaxStatusCode)
        {
            error = "StatusCode must be between 500 and 599.";
            return false;
        }

        fault = new ValidatedFault(
            service, faultType, request.LatencyMs, percent, statusCode, TimeSpan.FromSeconds(durationSeconds));
        return true;
    }
}