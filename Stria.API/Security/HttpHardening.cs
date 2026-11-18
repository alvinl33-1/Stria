using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;

namespace Stria.Security;

/// <summary>Per-caller fixed-window limits for the control-plane API ("RateLimits" section).</summary>
public sealed class ChaosRateLimitOptions
{
    public const string SectionName = "RateLimits";

    /// <summary>Reads per window. The dashboard polls every 3 s, i.e. 20 per minute per tab.</summary>
    public int ReadPermitLimit { get; set; } = 120;

    /// <summary>Fault injections/clears per window.</summary>
    public int MutatePermitLimit { get; set; } = 20;

    public int WindowSeconds { get; set; } = 60;
}

/// <summary>Request-size, rate-limit and response-header hardening for the Stria host.</summary>
public static class HttpHardening
{
    public const string ReadPolicy = "chaos-read";
    public const string MutatePolicy = "chaos-mutate";

    /// <summary>Largest accepted request body on /api/v1/chaos. A fault request is well under 1 KB.</summary>
    public const long MaxChaosBodyBytes = 16 * 1024;

    // 'wasm-unsafe-eval' is required by Blazor WebAssembly; 'unsafe-inline' styles cover the
    // inline width on the fault status bars. form-action allows the Entra sign-out redirect.
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; " +
        "frame-ancestors 'none'; form-action 'self' https://login.microsoftonline.com";

    public static IServiceCollection AddChaosRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ChaosRateLimitOptions>(configuration.GetSection(ChaosRateLimitOptions.SectionName));

        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(ReadPolicy, http => Partition(http, limits => limits.ReadPermitLimit));
            options.AddPolicy(MutatePolicy, http => Partition(http, limits => limits.MutatePermitLimit));
            options.OnRejected = async (context, ct) =>
            {
                var http = context.HttpContext;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    http.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                http.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Stria.RateLimiting")
                    .LogWarning("Rate limit exceeded for {Method} {Path}.", http.Request.Method, http.Request.Path);

                await WriteProblemAsync(http, StatusCodes.Status429TooManyRequests, "Too many requests.");
            };
        });
    }

    /// <summary>
    /// Partitions by authenticated object ID, falling back to the client IP. Must run after
    /// UseAuthorization so HttpContext.User reflects the endpoint's schemes (bearer included).
    /// </summary>
    private static RateLimitPartition<string> Partition(HttpContext http, Func<ChaosRateLimitOptions, int> permits)
    {
        var limits = http.RequestServices.GetRequiredService<IOptions<ChaosRateLimitOptions>>().Value;
        var oid = http.User.GetObjectId();
        var key = oid is not null
            ? $"user:{oid}"
            : $"ip:{http.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits(limits),
            Window = TimeSpan.FromSeconds(limits.WindowSeconds),
            QueueLimit = 0
        });
    }

    /// <summary>
    /// Caps request bodies on /api/v1/chaos. Middleware rather than an endpoint filter, because
    /// filters run after the JSON body has already been read and bound.
    /// </summary>
    public static IApplicationBuilder UseChaosBodyLimit(this IApplicationBuilder app) =>
        app.UseWhen(
            ctx => ctx.Request.Path.StartsWithSegments("/api/v1/chaos"),
            branch => branch.Use(async (ctx, next) =>
            {
                // Also bounds chunked bodies that carry no Content-Length (Kestrel enforces it).
                if (ctx.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
                    limit.MaxRequestBodySize = MaxChaosBodyBytes;

                if (ctx.Request.ContentLength > MaxChaosBodyBytes)
                {
                    await WriteProblemAsync(ctx, StatusCodes.Status413PayloadTooLarge, "Request body too large.");
                    return;
                }

                await next(ctx);
            }));

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (ctx, next) =>
        {
            var headers = ctx.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            // Report-only until the dashboard has been checked in a browser with no violations;
            // then rename the header to Content-Security-Policy to enforce it.
            headers["Content-Security-Policy-Report-Only"] = ContentSecurityPolicy;
            await next(ctx);
        });

    private static async Task WriteProblemAsync(HttpContext http, int status, string title)
    {
        http.Response.StatusCode = status;
        await http.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Type = "about:blank" }
        });
    }
}
