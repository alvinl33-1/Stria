using System.Diagnostics;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Npgsql;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Stria.Services;

/// <summary>
/// Spans for the operations an operator asks about later: who injected what, when the kill
/// switch changed, and whether a service read its directive. Tags carry the target service and
/// fault type only, never the actor, the reason or any credential.
/// </summary>
public static class StriaTelemetry
{
    public const string SourceName = "Stria";
    public const string ConnectionStringKey = "APPLICATIONINSIGHTS_CONNECTION_STRING";

    public const string TargetServiceTag = "stria.target_service";
    public const string FaultTypeTag = "stria.fault_type";
    public const string DirectiveFoundTag = "stria.directive.found";

    public static readonly ActivitySource Source = new(SourceName);

    /// <summary>
    /// Exports to Application Insights only when a connection string is configured (Azure).
    /// Locally the spans are created only if something listens, so they cost nothing.
    /// </summary>
    /// <returns>Whether the exporter was registered.</returns>
    public static bool AddStriaTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration[ConnectionStringKey]))
            return false;

        services.AddOpenTelemetry()
            .UseAzureMonitor() // reads APPLICATIONINSIGHTS_CONNECTION_STRING
            .ConfigureResource(resource => resource.AddService("stria-server"))
            .WithTracing(tracing => tracing.AddNpgsql().AddSource(SourceName));
        return true;
    }
}
