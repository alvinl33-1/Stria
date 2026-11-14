using Microsoft.Extensions.Options;
using Stria.SampleService;

var builder = WebApplication.CreateBuilder(args);

// Fail fast on bad configuration instead of polling a nonsense URL forever.
builder.Services.AddOptions<StriaOptions>()
    .Bind(builder.Configuration.GetSection(StriaOptions.SectionName))
    .Validate(
        o => !string.IsNullOrWhiteSpace(o.ServiceName) 
             && Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _),
        "Stria.API:ServiceName and an absolute Stria.API:BaseUrl are required.")
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<FaultState>();

builder.Services.AddHttpClient("stria", (sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<StriaOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    // a slow control plane must not stall the poll loop
    client.Timeout = TimeSpan.FromSeconds(2);
});

builder.Services.AddHostedService<FaultPoller>();

var app = builder.Build();

// Faults apply to everything except the health probe, so orchestration can still see the
// process is alive while it is being "broken". (Endpoints run after this middleware.)
app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/healthz"),
    branch => branch.UseMiddleware<FaultInjectionMiddleware>());

app.MapGet("/healthz", () => Results.Ok("ok"));

// Stand-in for real work: the thing you'd curl to observe an injected fault.
app.MapGet("/work", (IOptions<StriaOptions> options, TimeProvider time) =>
    Results.Ok(new { service = options.Value.ServiceName, ok = true, servedAt = time.GetUtcNow() }));

app.Run();
