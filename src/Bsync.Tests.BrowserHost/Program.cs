using Bsync.Tests.BrowserHost;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
var host = builder.Build();

Harness.Initialize(host.Services.GetRequiredService<IJSRuntime>(), new Uri(builder.HostEnvironment.BaseAddress));
await host.Services.GetRequiredService<IJSRuntime>().InvokeVoidAsync("bsyncHarnessStarted");
await host.RunAsync();
