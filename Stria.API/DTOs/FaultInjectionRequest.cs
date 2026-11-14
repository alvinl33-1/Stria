namespace Stria.DTOs;

// The three trailing parameters are new and optional, so existing callers keep compiling and
// existing JSON payloads keep working. TriggeredBy is still accepted but the server ignores it.
public record FaultInjectionRequest(
    string TargetService,
    string FaultType,
    int? LatencyMs = null,
    string TriggeredBy = "ManualDashboard",
    int? DurationSeconds = null,
    int? Percent = null,
    int? StatusCode = null
);