// Stria.Client/Program.cs
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net.Http;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Register a scoped HttpClient instance.
// BaseAddress automatically targets the exact server origin URL hosting the app.
builder.Services.AddScoped(sp => new HttpClient 
{ 
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) 
});

await builder.Build().RunAsync();