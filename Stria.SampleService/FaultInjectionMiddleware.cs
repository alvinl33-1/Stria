using Microsoft.Extensions.Options;
 
namespace Stria.SampleService;
 
/// <summary>
/// The data plane's enforcement point. For each request it asks FaultState what (if anything)
/// is active, then:
///   Latency      -> await Task.Delay (no thread is blocked), then continue normally.
///   HttpFailure  -> short-circuit with the configured 5xx status.
/// Anything else (CpuSpike) is passed through untouched, and no header claims otherwise.
/// </summary>
public class FaultInjectionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly FaultState _state;
    private readonly StriaOptions _options;
    private readonly ILogger<FaultInjectionMiddleware> _logger;
 
    private int _delayedInFlight;
 
    public FaultInjectionMiddleware(
        RequestDelegate next,
        FaultState state,
        IOptions<StriaOptions> options,
        ILogger<FaultInjectionMiddleware> logger)
    {
        _next = next;
        _state = state;
        _options = options.Value;
        _logger = logger;
    }
    
    public async Task InvokeAsync(HttpContext context)
    {
        var directive = _state.GetActive();
        if (directive is null || !IsSelected(directive.Percent))
        {
            await _next(context);
            return;
        }

        switch (directive.FaultType)
        {
            case "Latency" when directive.LatencyMs is int delayMs:
                context.Response.Headers["X-Stria-Fault"] = $"Latency";
                await DelayAsync(delayMs, context.RequestAborted);
                await _next(context);
                return;
            
            case "HttpFailure":
                context.Response.Headers["X-Stria-Fault"] = "HttpFailure";
                context.Response.StatusCode = directive.StatusCode ?? StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsJsonAsync(
                    new { error = "Fault injected by Stria.", fault = "HttpFailure" },
                    context.RequestAborted);
                return;
            
            default:
                await _next(context);
                return;
        }
    }
    
    /// <summary>Percent = share of requests affected. 100 means every request.</summary>
    private static bool IsSelected(int percent) =>
        percent >= 100 || Random.Shared.Next(100) < percent;

    private async Task DelayAsync(int delayMs, CancellationToken ct)
    {
        // Task.Delay doesn't hold a thread, but every parked request still holds memory and a
        // connection. Bound them so an injected delay can't take down the process it targets.
        if (Interlocked.Increment(ref _delayedInFlight) > _options.MaxConcurrentDelayed)
        {
            Interlocked.Decrement(ref _delayedInFlight);
            _logger.LogDebug("Delay cap reached ({Max}); serving this request without the injected delay.",
                _options.MaxConcurrentDelayed);
            return;
        }

        try
        {
            await Task.Delay(delayMs, ct);
        }
        finally
        {
            Interlocked.Decrement(ref _delayedInFlight);
        }
    }
}