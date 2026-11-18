using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace Stria.SampleService;

/// <summary>
/// Polls Stria for this service's directive. Failure handling is the point:
///   * 204 (no fault)  -> clear immediately.
///   * Network/HTTP failure -> keep the current lease but never extend it, so it expires on
///     schedule. A control-plane outage can therefore never leave a fault running forever.
/// </summary>
public sealed class FaultPoller : BackgroundService
{
    private readonly HttpClient _http;
    private readonly FaultState _state;
    private readonly StriaOptions _options;
    private readonly ILogger<FaultPoller> _logger;

    private Guid? _lastExperimentId;
    private bool _unreachable;

    public FaultPoller(
        IHttpClientFactory httpFactory,
        FaultState state,
        IOptions<StriaOptions> options,
        ILogger<FaultPoller> logger)
    {
        _http = httpFactory.CreateClient("stria");
        _state = state;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var url = $"/api/v1/chaos/directives/{Uri.EscapeDataString(_options.ServiceName)}";
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Max(250, _options.PollIntervalMs)));

        do
        {
            try
            {
                await PollOnceAsync(url, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!_unreachable)
                    _logger.LogWarning(ex, "Unable to poll Stria.API for {Service}; keeping the current fault lease until it expires.", _options.ServiceName);
                _unreachable = true;
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }


    private async Task PollOnceAsync(string url, CancellationToken stoppingToken)
    {
        using var response = await _http.GetAsync(url, stoppingToken);
        FaultDirective? directive = null;

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new HttpRequestException(
                $"Stria rejected this service's data-plane key ({(int)response.StatusCode}); check Stria:ServiceKey.",
                null, response.StatusCode);

        if (response.StatusCode != HttpStatusCode.NoContent)
        {
            response.EnsureSuccessStatusCode();
            directive = await response.Content.ReadFromJsonAsync<FaultDirective>(stoppingToken);
        }
        _state.Apply(directive);
        if (_unreachable)
        {
            _logger.LogInformation("Connection to Stria.API restored for {Service}.", _options.ServiceName);
            _unreachable = false;
        }
        LogTransition(directive);
    }

    private void LogTransition(FaultDirective? directive)
    {
        if (directive?.ExperimentId == _lastExperimentId)
            return;

        _lastExperimentId = directive?.ExperimentId;

        if (directive is null)
        {
            _logger.LogInformation("No active fault for {Service}", _options.ServiceName);
        }
        else if (directive.FaultType is "Latency" or "HttpFailure")
        {
            _logger.LogWarning("Applying {Fault} to {Service} for {Remaining}s",
                directive.FaultType, _options.ServiceName, directive.RemainingSeconds);
        }
        else
        {
            _logger.LogWarning(
                "{Fault} is active in Stria.API, but this sample service does not execute it yet.",
                directive.FaultType);
        }
    }
}
