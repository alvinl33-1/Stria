// Stria.Client/Program.cs
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Stria.Client.Security;
using Stria.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Same-origin HttpClient: the browser attaches the HttpOnly auth cookie automatically, so no
// token ever lives in WebAssembly memory.
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
});
builder.Services.AddScoped<ChaosApiClient>();

// The server serializes the signed-in user's name and roles into the page; the policies here
// only hide UI. The server enforces every policy again on each API call.
builder.Services.AddAuthorizationCore(ChaosPolicies.AddPolicies);
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthenticationStateDeserialization();

await builder.Build().RunAsync();
